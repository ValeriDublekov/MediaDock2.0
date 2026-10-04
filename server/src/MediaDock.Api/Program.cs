using MediaDock.Api.Catalog;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Favorites;
using MediaDock.Api.Health;
using MediaDock.Api.Middleware;
using MediaDock.Api.OscarAwards;
using MediaDock.Api.Operations;
using MediaDock.Api.Sources;
using MediaDock.Api.Versioning;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Ingestion;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddDbContext<MediaDockDbContext>(options =>
	options.UseNpgsql(builder.Configuration.GetConnectionString("MediaDock")
		?? throw new InvalidOperationException("ConnectionStrings:MediaDock must be configured.")));
builder.Services.AddIngestionInfrastructure();
builder.Services.AddScoped<MediaDock.Infrastructure.GoldenGlobes.GoldenGlobeDatasetImporter>();
builder.Services.AddScoped<BackgroundJobApiService>();
builder.Services.AddScoped<BackgroundJobScheduler>();
builder.Services.AddHostedService<BackgroundJobDispatcher>();
builder.Services.AddScoped<ICatalogApiService, CatalogApiService>();
builder.Services.AddScoped<FavoriteApiService>();
builder.Services.AddScoped<IOscarApiService, OscarApiService>();
builder.Services.AddScoped<ISourceSettingsApiService, SourceSettingsApiService>();
builder.Services.AddScoped<IOperationalHistoryApiService, OperationalHistoryApiService>();
builder.Services.AddScoped<IReadinessService, ReadinessService>();

var app = builder.Build();
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

app.UseExceptionHandler();
app.UseStatusCodePages();

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
app.MapCatalogEndpoints();
app.MapFavoriteEndpoints();
app.MapOscarEndpoints();
app.MapSourceSettingsEndpoints();
app.MapOperationalHistoryEndpoints();
app.MapBackgroundJobEndpoints(maximumUploadBytes);
app.MapVersionEndpoints();

if (app.Environment.IsProduction())
{
	app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>Entry point exposed for API integration tests.</summary>
public partial class Program;
