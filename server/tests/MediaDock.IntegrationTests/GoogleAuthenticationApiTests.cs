using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MediaDock.Api.Administration;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.Favorites;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class GoogleAuthenticationApiTests
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
        Assert.Equal(HttpStatusCode.NotFound, seriesCatalogAddResponse.StatusCode);

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

    [Fact]
    public async Task AnonymousModeRemainsAvailableAndStateChangesRequireAntiforgery()
    {
        using var factory = new ApiFactory("Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test");
        using var client = factory.CreateClient();

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(session);
        Assert.False(session.Authenticated);
        Assert.Equal("anonymous", session.AccountState);
        Assert.False(session.AuthorizationEnforced);

        using var versionResponse = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);

        using var loginResponse = await client.GetAsync("/api/auth/google/login");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, loginResponse.StatusCode);

        using var logoutResponse = await client.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);

        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.Contains(
            tokenResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("MediaDock.Antiforgery=", StringComparison.Ordinal)
                && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        var token = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        Assert.NotNull(token);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", token.RequestToken);
        using var validLogoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, validLogoutResponse.StatusCode);
    }

    [Fact]
    public void CallbackConfigurationAllowsOnlyLocalhostHttpOrHttpsHostname()
    {
        var localSettings = CreateSettings("http://localhost:8080/signin-oidc");
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("localhost", 8080);
        Assert.True(localSettings.MatchesRequestOrigin(request));

        request.Host = new HostString("127.0.0.1", 8080);
        Assert.False(localSettings.MatchesRequestOrigin(request));
        Assert.Throws<InvalidOperationException>(() => CreateSettings("http://192.168.1.20/signin-oidc"));

        var secureSettings = CreateSettings("https://mediadock.example/signin-oidc");
        request.Scheme = "https";
        request.Host = new HostString("mediadock.example");
        Assert.True(secureSettings.MatchesRequestOrigin(request));
    }

    [Fact]
    public async Task ForwardedHttpsSchemeIsAcceptedOnlyFromConfiguredProxyNetwork()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TrustedForwardedHeaders.TrustedNetworkConfigurationKey] = "172.20.0.0/16"
            })
            .Build();
        var options = TrustedForwardedHeaders.CreateOptions(configuration);
        Assert.NotNull(options);

        using var loggerFactory = LoggerFactory.Create(_ => { });
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options));

        var trustedContext = CreateForwardedRequest("172.20.0.3");
        await middleware.Invoke(trustedContext);
        Assert.Equal("https", trustedContext.Request.Scheme);

        var untrustedContext = CreateForwardedRequest("192.168.1.10");
        await middleware.Invoke(untrustedContext);
        Assert.Equal("http", untrustedContext.Request.Scheme);
    }

    private static DefaultHttpContext CreateForwardedRequest(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        return context;
    }

    [Fact]
    public async Task VerifiedEmailMatchRequiresCsrfProtectedOneTimeAccountLink()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_link_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new User
            {
                NormalizedEmail = "person@example.com",
                GivenName = "Stored",
                FamilyName = "Name",
                Status = "active",
                Role = "user",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousCatalogResponse = await anonymousClient.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, anonymousCatalogResponse.StatusCode);

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();

        using var catalogResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(session);
        Assert.True(session.Authenticated);
        Assert.Equal("link_confirmation_required", session.AccountState);
        Assert.Equal("https://accounts.google.com", session.Identity?.Issuer);
        Assert.Equal("google-subject-1", session.Identity?.Subject);
        Assert.Equal("person@example.com", session.User?.Email);
        Assert.Equal("Stored", session.User?.GivenName);

        using var missingTokenResponse = await client.PostAsync("/api/auth/google/link", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        var token = await GetAntiforgeryTokenAsync(client);
        using var linkRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        linkRequest.Headers.Add("RequestVerificationToken", token);
        using var linkResponse = await client.SendAsync(linkRequest);
        Assert.Equal(HttpStatusCode.NoContent, linkResponse.StatusCode);

        using var repeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        repeatRequest.Headers.Add("RequestVerificationToken", token);
        using var repeatResponse = await client.SendAsync(repeatRequest);
        Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);

        using var linkedSessionResponse = await client.GetAsync("/api/auth/session");
        var linkedSession = await linkedSessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(linkedSession);
        Assert.Equal("linked", linkedSession.AccountState);
        Assert.Equal("Stored", linkedSession.User?.GivenName);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", token);
        using var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Contains(
            logoutResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-MediaDock.Session=", StringComparison.Ordinal)
                && value.Contains("secure", StringComparison.OrdinalIgnoreCase));

        await using var verifyDb = new MediaDockDbContext(options);
        var identity = await verifyDb.ExternalIdentities.AsNoTracking().SingleAsync();
        Assert.Equal("https://accounts.google.com", identity.Issuer);
        Assert.Equal("google-subject-1", identity.Subject);
    }

    [Fact]
    public async Task RegistrationRequestUsesValidatedIdentityAndIsIdempotent()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_registration_request_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var missingTokenResponse = await client.PostAsync(
            "/api/auth/registration-requests",
            new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests")
        {
            Content = new StringContent(
                "{\"email\":\"attacker@example.com\",\"givenName\":\"Forged\",\"subject\":\"forged-subject\"}",
                System.Text.Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RegistrationRequestResponse>();
        Assert.NotNull(result);
        Assert.Equal("pending", result.Status);

        using var repeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests");
        repeatRequest.Headers.Add("RequestVerificationToken", token);
        using var repeatResponse = await client.SendAsync(repeatRequest);
        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        var repeatedResult = await repeatResponse.Content.ReadFromJsonAsync<RegistrationRequestResponse>();
        Assert.Equal(result, repeatedResult);

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.Equal("linked", session?.AccountState);
        Assert.Equal("person@example.com", session?.User?.Email);
        Assert.Equal("Google", session?.User?.GivenName);
        Assert.Equal("Profile", session?.User?.FamilyName);
        Assert.Equal("pending", session?.RegistrationRequest?.Status);

        await using var verifyDb = new MediaDockDbContext(options);
        Assert.Equal("person@example.com", await verifyDb.Users.Select(user => user.NormalizedEmail).SingleAsync());
        Assert.Equal("Google", await verifyDb.Users.Select(user => user.GivenName).SingleAsync());
        Assert.Equal("Profile", await verifyDb.Users.Select(user => user.FamilyName).SingleAsync());
        Assert.Equal("https://accounts.google.com", await verifyDb.ExternalIdentities.Select(identity => identity.Issuer).SingleAsync());
        Assert.Equal("google-subject-1", await verifyDb.ExternalIdentities.Select(identity => identity.Subject).SingleAsync());
        Assert.Single(await verifyDb.RegistrationRequests.ToListAsync());
    }

    [Fact]
    public async Task UnverifiedEmailIsRejectedAndMissingNamesRemainIncomplete()
    {
        using var unverifiedFactory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            CreatePrincipal(emailVerified: false));
        using var unverifiedClient = unverifiedFactory.CreateClient();

        using var rejectedResponse = await unverifiedClient.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedResponse.StatusCode);
        Assert.Contains("email_not_verified", await rejectedResponse.Content.ReadAsStringAsync());
        Assert.True(rejectedResponse.Headers.TryGetValues("X-MediaDock-TraceId", out var sessionTraceIds));
        Assert.NotEmpty(sessionTraceIds!);

        using var logoutResponse = await unverifiedClient.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);
        Assert.True(logoutResponse.Headers.TryGetValues("X-MediaDock-TraceId", out var logoutTraceIds));
        Assert.NotEmpty(logoutTraceIds!);

        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_names_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var incompleteFactory = new ApiFactory(connectionString, CreatePrincipal(includeNames: false));
        using var incompleteClient = incompleteFactory.CreateClient();
        using var incompleteResponse = await incompleteClient.GetAsync("/api/auth/session");
        var incompleteSession = await incompleteResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, incompleteResponse.StatusCode);
        Assert.NotNull(incompleteSession?.Identity);
        Assert.False(incompleteSession.Identity.ProfileComplete);
        Assert.Null(incompleteSession.Identity.GivenName);
        Assert.Equal("unmatched", incompleteSession.AccountState);

        var token = await GetAntiforgeryTokenAsync(incompleteClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests");
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await incompleteClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verifyDb = new MediaDockDbContext(options);
        Assert.Empty(await verifyDb.Users.ToListAsync());
        Assert.Empty(await verifyDb.RegistrationRequests.ToListAsync());
    }

    [Theory]
    [InlineData(0, "missing")]
    [InlineData(2, "multiple")]
    public async Task InvalidEmailVerificationClaimShapeIsLoggedWithoutClaimValues(
        int claimCount,
        string expectedClaimState)
    {
        using var loggerProvider = new TestLoggerProvider();
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            CreatePrincipal(
                emailVerificationClaimCount: claimCount,
                conflictingEmailVerificationClaims: claimCount > 1),
            loggerProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var warning = Assert.Single(loggerProvider.Messages, message =>
            message.Contains("ReasonCode missing_or_ambiguous_email_verification", StringComparison.Ordinal));
        Assert.Contains($"EmailVerificationClaimCount {claimCount}", warning, StringComparison.Ordinal);
        Assert.Contains($"EmailVerificationClaimState {expectedClaimState}", warning, StringComparison.Ordinal);
        Assert.Contains("TraceId", warning, StringComparison.Ordinal);
        if (claimCount > 1)
        {
            Assert.Contains("EmailVerificationClaimValuesAgree False", warning, StringComparison.Ordinal);
        }

        Assert.False(warning.Contains("Person@Example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdenticalEmailVerificationClaimsAreAcceptedButConflictingClaimsAreRejected()
    {
        var accepted = ValidatedGoogleIdentity.TryCreate(
            CreatePrincipal(emailVerificationClaimCount: 2),
            out var identity,
            out var acceptedFailureCode);

        Assert.True(accepted);
        Assert.NotNull(identity);
        Assert.Empty(acceptedFailureCode);

        var rejected = ValidatedGoogleIdentity.TryCreate(
            CreatePrincipal(emailVerificationClaimCount: 2, conflictingEmailVerificationClaims: true),
            out _,
            out var rejectedFailureCode);

        Assert.False(rejected);
        Assert.Equal("missing_or_ambiguous_email_verification", rejectedFailureCode);
    }

    [Fact]
    public async Task LinkIsRejectedWhenEmailBelongsToAnAlreadyLinkedAccount()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_conflict_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var user = new User
            {
                NormalizedEmail = "person@example.com",
                GivenName = "Stored",
                FamilyName = "Name",
                Status = "active",
                Role = "user",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            user.ExternalIdentities.Add(new ExternalIdentity
            {
                User = user,
                Issuer = "https://accounts.google.com",
                Subject = "other-google-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();
        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.Equal("account_conflict", session?.AccountState);

        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UserAdministrationRequiresAnActiveAdminAndApprovalActivatesTheRequester()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_user_admin_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long requestId;
        long adminId;
        long requesterId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                NormalizedEmail = "admin@example.com",
                GivenName = "Admin",
                FamilyName = "Account",
                Status = "active",
                Role = "admin",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            admin.ExternalIdentities.Add(new ExternalIdentity
            {
                User = admin,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "admin-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var requester = new User
            {
                NormalizedEmail = "new@example.com",
                GivenName = "New",
                FamilyName = "User",
                Status = "pending",
                Role = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            requester.ExternalIdentities.Add(new ExternalIdentity
            {
                User = requester,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "requester-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var registrationRequest = new RegistrationRequest
            {
                User = requester,
                RequestedAt = DateTimeOffset.UtcNow,
                Status = "pending"
            };
            db.Users.AddRange(admin, requester);
            db.RegistrationRequests.Add(registrationRequest);
            await db.SaveChangesAsync();
            requestId = registrationRequest.Id;
            adminId = admin.Id;
            requesterId = requester.Id;
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        var anonymousUsers = await anonymousResponse.Content.ReadFromJsonAsync<AdminUsersPageResponse>();
        Assert.NotNull(anonymousUsers);
        Assert.Equal(2, anonymousUsers.TotalCount);
        Assert.Contains(anonymousUsers.Items, user => user.Email == "new@example.com");

        using var regularUserFactory = new ApiFactory(connectionString, CreatePrincipal(subject: "requester-subject"));
        using var regularUserClient = regularUserFactory.CreateClient();
        using var regularUserResponse = await regularUserClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, regularUserResponse.StatusCode);

        using var adminFactory = new ApiFactory(connectionString, CreatePrincipal(email: "admin@example.com", subject: "admin-subject"));
        using var adminClient = adminFactory.CreateClient();
        using var listResponse = await adminClient.GetAsync("/api/admin/users?state=requested");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var users = await listResponse.Content.ReadFromJsonAsync<AdminUsersPageResponse>();
        Assert.NotNull(users);
        var listedRequester = Assert.Single(users.Items);
        Assert.Equal("new@example.com", listedRequester.Email);
        Assert.Equal("pending", listedRequester.RegistrationRequest?.Status);

        var token = await GetAntiforgeryTokenAsync(anonymousClient);
        using var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/registration-requests/{requestId}/decision")
        {
            Content = JsonContent.Create(new AdminRegistrationDecisionRequest("approve"))
        };
        approveRequest.Headers.Add("RequestVerificationToken", token);
        using var approveResponse = await anonymousClient.SendAsync(approveRequest);
        Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

        await using (var verifyDb = new MediaDockDbContext(options))
        {
            var approvedUser = await verifyDb.Users.SingleAsync(user => user.NormalizedEmail == "new@example.com");
            var approvedRequest = await verifyDb.RegistrationRequests.SingleAsync(request => request.Id == requestId);
            Assert.Equal("active", approvedUser.Status);
            Assert.Equal("user", approvedUser.Role);
            Assert.Equal("approved", approvedRequest.Status);
            Assert.Equal("open-mode", approvedRequest.DecidedBy);
            Assert.NotNull(approvedRequest.DecidedAt);
            Assert.Equal(1, await verifyDb.Users.CountAsync(user => user.Id == adminId && user.Role == "admin"));
        }

        var statusToken = await GetAntiforgeryTokenAsync(anonymousClient);
        using var deactivateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{requesterId}/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        deactivateRequest.Headers.Add("RequestVerificationToken", statusToken);
        using var deactivateResponse = await anonymousClient.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        await using var statusDb = new MediaDockDbContext(options);
        Assert.Equal("deactivated", await statusDb.Users
            .Where(user => user.Id == requesterId)
            .Select(user => user.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task EnforcedUserAdministrationRequiresServerResolvedActiveAdmin()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_admin_policy_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            foreach (var (email, subject, role, status) in new[]
            {
                ("admin@example.com", "admin-subject", "admin", "active"),
                ("user@example.com", "user-subject", "user", "active"),
                ("disabled@example.com", "disabled-subject", "admin", "deactivated")
            })
            {
                var user = new User
                {
                    NormalizedEmail = email,
                    GivenName = "Test",
                    FamilyName = "User",
                    Status = status,
                    Role = role,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                user.ExternalIdentities.Add(new ExternalIdentity
                {
                    User = user,
                    Issuer = GoogleSignInSettings.GoogleIssuer,
                    Subject = subject,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                db.Users.Add(user);
            }

            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString, accessControlEnforced: true);
        Assert.True(anonymousFactory.Services.GetRequiredService<AccessControlSettings>().Enforced);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        using var anonymousDecisionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/registration-requests/1/decision")
        {
            Content = JsonContent.Create(new AdminRegistrationDecisionRequest("approve"))
        };
        using var anonymousDecisionResponse = await anonymousClient.SendAsync(anonymousDecisionRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDecisionResponse.StatusCode);
        using var anonymousStatusRequest = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/1/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        using var anonymousStatusResponse = await anonymousClient.SendAsync(anonymousStatusRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatusResponse.StatusCode);
        using var anonymousRecommendationSettingsResponse = await anonymousClient.GetAsync("/api/settings/recommendations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRecommendationSettingsResponse.StatusCode);
        using var anonymousCatalogResponse = await anonymousClient.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCatalogResponse.StatusCode);
        using var anonymousHealthResponse = await anonymousClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, anonymousHealthResponse.StatusCode);

        using var unknownFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "unknown@example.com", subject: "unknown-subject"),
            accessControlEnforced: true);
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownResponse = await unknownClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        using var deactivatedFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "disabled@example.com", subject: "disabled-subject"),
            accessControlEnforced: true);
        using var deactivatedClient = deactivatedFactory.CreateClient();
        using var deactivatedResponse = await deactivatedClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedResponse.StatusCode);

        using var userFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "user@example.com", subject: "user-subject"),
            accessControlEnforced: true);
        using var userClient = userFactory.CreateClient();
        using var userCatalogResponse = await userClient.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, userCatalogResponse.StatusCode);
        using var userResponse = await userClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
        using var userDecisionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/registration-requests/1/decision")
        {
            Content = JsonContent.Create(new AdminRegistrationDecisionRequest("approve"))
        };
        using var userDecisionResponse = await userClient.SendAsync(userDecisionRequest);
        Assert.Equal(HttpStatusCode.Forbidden, userDecisionResponse.StatusCode);
        using var userStatusRequest = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/1/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        using var userStatusResponse = await userClient.SendAsync(userStatusRequest);
        Assert.Equal(HttpStatusCode.Forbidden, userStatusResponse.StatusCode);
        using var userSettingsResponse = await userClient.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Forbidden, userSettingsResponse.StatusCode);
        using var userRecommendationSettingsResponse = await userClient.GetAsync("/api/settings/recommendations");
        Assert.Equal(HttpStatusCode.Forbidden, userRecommendationSettingsResponse.StatusCode);
        using var userSettingsWrite = new HttpRequestMessage(HttpMethod.Put, "/api/settings")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        using var userSettingsWriteResponse = await userClient.SendAsync(userSettingsWrite);
        Assert.Equal(HttpStatusCode.Forbidden, userSettingsWriteResponse.StatusCode);
        using var userHistoryResponse = await userClient.GetAsync("/api/scan-runs");
        Assert.Equal(HttpStatusCode.Forbidden, userHistoryResponse.StatusCode);
        using var userDeploymentResponse = await userClient.GetAsync("/api/deployment");
        Assert.Equal(HttpStatusCode.Forbidden, userDeploymentResponse.StatusCode);
        using var userScanRequest = new HttpRequestMessage(HttpMethod.Post, "/api/background-jobs/scans");
        using var userScanResponse = await userClient.SendAsync(userScanRequest);
        Assert.Equal(HttpStatusCode.Forbidden, userScanResponse.StatusCode);

        using var adminFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "admin@example.com", subject: "admin-subject"),
            accessControlEnforced: true);
        using var adminClient = adminFactory.CreateClient();
        using var adminResponse = await adminClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        using var adminSessionResponse = await adminClient.GetAsync("/api/auth/session");
        var adminSession = await adminSessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.True(adminSession?.AuthorizationEnforced);
        Assert.Equal("admin", adminSession?.User?.Role);
        using var adminSettingsResponse = await adminClient.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, adminSettingsResponse.StatusCode);
        using var adminRecommendationSettingsResponse = await adminClient.GetAsync("/api/settings/recommendations");
        Assert.Equal(HttpStatusCode.OK, adminRecommendationSettingsResponse.StatusCode);
        using var adminFavoritesResponse = await adminClient.GetAsync("/api/favorites?userId=2");
        Assert.Equal(HttpStatusCode.OK, adminFavoritesResponse.StatusCode);
        using var userFavoritesResponse = await userClient.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.OK, userFavoritesResponse.StatusCode);
        using var ratingsContent = new MultipartFormDataContent();
        var ratingsFile = new ByteArrayContent(
            System.Text.Encoding.UTF8.GetBytes("[{\"id\":\"tt1234567\",\"rating\":8}]"));
        ratingsFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        ratingsContent.Add(ratingsFile, "File", "ratings.json");
        var adminRatingsToken = await GetAntiforgeryTokenAsync(adminClient);
        using var adminRatingsRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/personal-ratings/import?userId=2")
        {
            Content = ratingsContent
        };
        adminRatingsRequest.Headers.Add("RequestVerificationToken", adminRatingsToken);
        using var adminRatingsResponse = await adminClient.SendAsync(adminRatingsRequest);
        Assert.True(
            adminRatingsResponse.StatusCode == HttpStatusCode.OK,
            await adminRatingsResponse.Content.ReadAsStringAsync());

        await using var verifyRatingsDb = new MediaDockDbContext(options);
        var adminUserId = await verifyRatingsDb.Users
            .Where(user => user.NormalizedEmail == "admin@example.com")
            .Select(user => user.Id)
            .SingleAsync();
        var otherUserId = await verifyRatingsDb.Users
            .Where(user => user.NormalizedEmail == "user@example.com")
            .Select(user => user.Id)
            .SingleAsync();
        var importedRating = await verifyRatingsDb.PersonalRatings.SingleAsync(rating => rating.ImdbId == "tt1234567");
        Assert.Equal(adminUserId, importedRating.UserId);
        Assert.NotEqual(otherUserId, importedRating.UserId);

        await using var roleChangeDb = new MediaDockDbContext(options);
        await roleChangeDb.Users
            .Where(user => user.NormalizedEmail == "admin@example.com")
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Role, "user"));
        using var demotedResponse = await adminClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, demotedResponse.StatusCode);
        using var demotedSessionResponse = await adminClient.GetAsync("/api/auth/session");
        var demotedSession = await demotedSessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.Equal("user", demotedSession?.User?.Role);
    }

    [Fact]
    public async Task EnforcedAdministrationDoesNotTreatMissingActorAsOpenMode()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_admin_actor_guard_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        var user = new User
        {
            NormalizedEmail = "pending@example.com",
            GivenName = "Pending",
            FamilyName = "User",
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var request = new RegistrationRequest
        {
            User = user,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };
        db.Users.Add(user);
        db.RegistrationRequests.Add(request);
        await db.SaveChangesAsync();

        var service = new UserAdministrationApiService(db, new AccessControlSettings(true));
        Assert.Equal(
            UserAdministrationResult.Forbidden,
            await service.DecideRegistrationRequestAsync(request.Id, "approve", null, CancellationToken.None));
        Assert.Equal(
            UserAdministrationResult.Forbidden,
            await service.SetUserStatusAsync(user.Id, "deactivated", null, CancellationToken.None));
        Assert.Equal("pending", await db.Users.Where(candidate => candidate.Id == user.Id)
            .Select(candidate => candidate.Status)
            .SingleAsync());
        Assert.Equal("pending", await db.RegistrationRequests.Where(candidate => candidate.Id == request.Id)
            .Select(candidate => candidate.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task LastActiveAdministratorCannotBeDeactivated()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_last_admin_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long adminId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                NormalizedEmail = "admin@example.com",
                GivenName = "Admin",
                FamilyName = "Account",
                Status = "active",
                Role = "admin",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            admin.ExternalIdentities.Add(new ExternalIdentity
            {
                User = admin,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "admin-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            adminId = admin.Id;
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal(email: "admin@example.com", subject: "admin-subject"));
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{adminId}/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verifyDb = new MediaDockDbContext(options);
        var adminUser = await verifyDb.Users.SingleAsync(user => user.Id == adminId);
        Assert.Equal("active", adminUser.Status);
        Assert.Equal("admin", adminUser.Role);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return token!.RequestToken;
    }

    private static ClaimsPrincipal CreatePrincipal(
        bool emailVerified = true,
        bool includeNames = true,
        string email = "Person@Example.com",
        string subject = "google-subject-1",
        int emailVerificationClaimCount = 1,
        bool conflictingEmailVerificationClaims = false)
    {
        var claims = new List<Claim>
        {
            new(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
            new("sub", subject),
            new("email", email)
        };
        for (var index = 0; index < emailVerificationClaimCount; index++)
        {
            var claimValue = conflictingEmailVerificationClaims && index > 0
                ? emailVerified ? "false" : "true"
                : emailVerified ? "true" : "false";
            claims.Add(new Claim("email_verified", claimValue));
        }

        if (includeNames)
        {
            claims.Add(new Claim("given_name", "Google"));
            claims.Add(new Claim("family_name", "Profile"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test-google"));
    }

    private static GoogleSignInSettings CreateSettings(string callbackUri)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "test-client",
                ["Authentication:Google:ClientSecret"] = "test-secret",
                ["Authentication:Google:CallbackUri"] = callbackUri
            })
            .Build();
        return GoogleSignInSettings.FromConfiguration(configuration);
    }

    private sealed class ApiFactory(
        string connectionString,
        ClaimsPrincipal? principal = null,
        ILoggerProvider? loggerProvider = null,
        bool accessControlEnforced = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString,
                    ["Authentication:Google:Enabled"] = "false",
                    ["AccessControl:Enforced"] = accessControlEnforced.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication()
                    .AddScheme<TestGoogleOptions, TestGoogleHandler>(TestGoogleOptions.Scheme, options =>
                    {
                        options.Principal = principal;
                    });
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.DefaultAuthenticateScheme = TestGoogleOptions.Scheme);
            });
        }
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class TestLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class TestGoogleOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "TestGoogle";
        public ClaimsPrincipal? Principal { get; set; }
    }

    private sealed class TestGoogleHandler(
        IOptionsMonitor<TestGoogleOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<TestGoogleOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.Principal is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(Options.Principal, Scheme.Name)));
        }
    }
}