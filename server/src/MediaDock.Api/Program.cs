using MediaDock.Api.Catalog;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Favorites;
using MediaDock.Api.Health;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Api.Middleware;
using MediaDock.Api.OscarAwards;
using MediaDock.Api.Operations;
using MediaDock.Api.PersonalRatings;
using MediaDock.Api.Sources;
using MediaDock.Api.Versioning;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Ingestion;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;

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
builder.Services.AddScoped<PersonalRatingsApiService>();
builder.Services.AddScoped<IOscarApiService, OscarApiService>();
builder.Services.AddScoped<IGoldenGlobeApiService, GoldenGlobeApiService>();
builder.Services.AddScoped<ISourceSettingsApiService, SourceSettingsApiService>();
builder.Services.AddScoped<IOperationalHistoryApiService, OperationalHistoryApiService>();
builder.Services.AddScoped<IReadinessService, ReadinessService>();
builder.Services.AddHttpClient<DeploymentControlApiService>(client =>
	client.BaseAddress = new Uri("http://mediadock-deploy-control"))
	.ConfigurePrimaryHttpMessageHandler(serviceProvider => new SocketsHttpHandler
	{
		ConnectCallback = async (_, cancellationToken) =>
		{
			var configuration = serviceProvider.GetRequiredService<IConfiguration>();
			var socketPath = configuration["DeploymentControl:SocketPath"]
				?? "/run/mediadock-next-deploy-control/control.sock";
			var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			try
			{
				await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}
	});

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
app.MapPersonalRatingsEndpoints();
app.MapOscarEndpoints();
app.MapGoldenGlobeEndpoints();
app.MapSourceSettingsEndpoints();
app.MapOperationalHistoryEndpoints();
app.MapBackgroundJobEndpoints(maximumUploadBytes);
app.MapDeploymentControlEndpoints();
app.MapVersionEndpoints();

if (app.Environment.IsProduction())
{
	app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>Entry point exposed for API integration tests.</summary>
public partial class Program;
