using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Administration;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediaDock.IntegrationTests.GoogleAuthenticationApiTestSupport;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class UserAdministrationApiTests
{

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
}
