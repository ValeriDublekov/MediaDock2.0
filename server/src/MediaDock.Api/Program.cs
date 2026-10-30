using MediaDock.Api.Catalog;
using MediaDock.Api.Awards;
using MediaDock.Api.Authentication;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.DependencyInjection;
using MediaDock.Api.Favorites;
using MediaDock.Api.Administration;
using MediaDock.Api.Health;
using MediaDock.Api.IgnoredMovies;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Api.Middleware;
using MediaDock.Api.OscarAwards;
using MediaDock.Api.Operations;
using MediaDock.Api.PersonalRatings;
using MediaDock.Api.Recommendations;
using MediaDock.Api.Sources;
using MediaDock.Api.Versioning;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Users;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediaDockApi(builder.Configuration, builder.Environment);
var app = builder.Build();
var isOneShotHostOperation = app.Configuration.GetValue<bool>("migrate")
	|| app.Configuration.GetValue<bool>("bootstrap-admin");
if (app.Environment.IsProduction() && !isOneShotHostOperation)
{
	if (app.Configuration.GetValue<bool>("Authentication:LocalMock:Enabled"))
	{
		throw new InvalidOperationException(
			"Authentication:LocalMock:Enabled is only supported in the Development environment.");
	}

	if (!app.Configuration.GetValue<bool>("Authentication:Google:Enabled"))
	{
		throw new InvalidOperationException("Production requires Google sign-in to be enabled.");
	}

	if (!app.Configuration.GetValue<bool>("AccessControl:Enforced"))
	{
		throw new InvalidOperationException("Production requires AccessControl:Enforced=true.");
	}
}

var forwardedHeadersOptions = TrustedForwardedHeaders.CreateOptions(builder.Configuration);
if (forwardedHeadersOptions is not null)
{
	app.UseForwardedHeaders(forwardedHeadersOptions);
}

var maximumUploadBytes = Math.Clamp(
	builder.Configuration.GetValue("BackgroundJobs:MaxUploadBytes", 10 * 1024 * 1024),
	1,
	100 * 1024 * 1024);

if (builder.Configuration.GetValue<bool>("migrate"))
{
	await using var scope = app.Services.CreateAsyncScope();
	await scope.ServiceProvider.GetRequiredService<MediaDockDbContext>()
		.Database.MigrateAsync();
	return;
}

if (builder.Configuration.GetValue<bool>("bootstrap-admin"))
{
	await using var scope = app.Services.CreateAsyncScope();
	var profile = new InitialAdminProfile(
		GetRequiredBootstrapValue("BootstrapAdmin:Email"),
		GetRequiredBootstrapValue("BootstrapAdmin:GivenName"),
		GetRequiredBootstrapValue("BootstrapAdmin:FamilyName"),
		GetRequiredBootstrapValue("BootstrapAdmin:Issuer"),
		GetRequiredBootstrapValue("BootstrapAdmin:Subject"));
	await scope.ServiceProvider.GetRequiredService<InitialAdminBootstrapper>()
		.BootstrapAsync(profile);
	return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsProduction())
{
	app.UseDefaultFiles();
	app.UseStaticFiles();
}

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
app.MapUserAdministrationEndpoints();
app.MapMovieTablesDashboardEndpoints();
app.MapCatalogEndpoints();
app.MapTorrentTitleMatchEndpoints();
app.MapMovieAwardsEndpoints();
app.MapFavoriteEndpoints();
app.MapIgnoredMoviesEndpoints();
app.MapPersonalRatingsEndpoints();
app.MapOscarEndpoints();
app.MapGoldenGlobeEndpoints();
app.MapSourceSettingsEndpoints();
app.MapRecommendationSettingsEndpoints();
app.MapMovieRecommendationsEndpoints();
app.MapOperationalHistoryEndpoints();
app.MapBackgroundJobEndpoints(maximumUploadBytes);
app.MapDeploymentControlEndpoints();
app.MapVersionEndpoints();

if (app.Environment.IsProduction())
{
	app.MapFallbackToFile("index.html");
}

app.Run();

string GetRequiredBootstrapValue(string key) =>
	builder.Configuration[key] is { Length: > 0 } value
		? value
		: throw new InvalidOperationException($"{key} must be configured for administrator bootstrap.");

/// <summary>Entry point exposed for API integration tests.</summary>
public partial class Program;
