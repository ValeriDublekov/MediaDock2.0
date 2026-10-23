using System.Net.Sockets;
using MediaDock.Api.Administration;
using MediaDock.Api.Awards;
using MediaDock.Api.Authentication;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Catalog;
using MediaDock.Api.Favorites;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Api.Health;
using MediaDock.Api.IgnoredMovies;
using MediaDock.Api.Middleware;
using MediaDock.Api.Operations;
using MediaDock.Api.OscarAwards;
using MediaDock.Api.PersonalRatings;
using MediaDock.Api.Recommendations;
using MediaDock.Api.Sources;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Ingestion;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
	public static IServiceCollection AddMediaDockApi(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddOpenApi();
		services.AddValidation();
		services.AddProblemDetails();
		services.AddExceptionHandler<ApiExceptionHandler>();
		services.AddGoogleSignIn(configuration);
		services.AddAccessControl(configuration);
		services.AddDbContext<MediaDockDbContext>(options =>
			options.UseNpgsql(configuration.GetConnectionString("MediaDock")
				?? throw new InvalidOperationException("ConnectionStrings:MediaDock must be configured.")));
		services.AddScoped<InitialAdminBootstrapper>();
		services.AddScoped<UserAdministrationApiService>();
		services.AddIngestionInfrastructure();
		services.AddScoped<GoldenGlobeDatasetImporter>();
		services.AddScoped<BackgroundJobApiService>();
		services.AddScoped<BackgroundJobScheduler>();
		services.AddKeyedScoped<IBackgroundJobHandler, RssScanBackgroundJobHandler>("rss_scan");
		services.AddKeyedScoped<IBackgroundJobHandler, OscarImportBackgroundJobHandler>("oscar_import");
		services.AddKeyedScoped<IBackgroundJobHandler, GoldenGlobeImportBackgroundJobHandler>("golden_globe_import");
		services.AddKeyedScoped<IBackgroundJobHandler, OscarEnrichmentBackgroundJobHandler>("oscar_enrichment");
		services.AddKeyedScoped<IBackgroundJobHandler, GoldenGlobeEnrichmentBackgroundJobHandler>("golden_globe_enrichment");
		services.AddKeyedScoped<IBackgroundJobHandler, ImdbTitleEnrichmentBackgroundJobHandler>("imdb_title_enrichment");
		services.AddKeyedScoped<IBackgroundJobHandler, GoldenGlobeManualRefreshBackgroundJobHandler>("golden_globe_manual_refresh");
		services.AddHostedService<BackgroundJobDispatcher>();
		services.AddScoped<ICatalogApiService, CatalogApiService>();
		services.AddScoped<ITorrentTitleMatchApiService, TorrentTitleMatchApiService>();
		services.AddScoped<IMovieAwardsApiService, MovieAwardsApiService>();
		services.AddScoped<FavoriteApiService>();
		services.AddScoped<IgnoredMoviesApiService>();
		services.AddScoped<PersonalRatingsApiService>();
		services.AddScoped<MovieRecommendationsApiService>();
		services.AddScoped<IOscarApiService, OscarApiService>();
		services.AddScoped<IGoldenGlobeApiService, GoldenGlobeApiService>();
		services.AddScoped<ISourceSettingsApiService, SourceSettingsApiService>();
		services.AddScoped<IRecommendationSettingsApiService, RecommendationSettingsApiService>();
		services.AddScoped<IOperationalHistoryApiService, OperationalHistoryApiService>();
		services.AddScoped<IReadinessService, ReadinessService>();
		services.AddHttpClient<DeploymentControlApiService>(client =>
			client.BaseAddress = new Uri("http://mediadock-deploy-control"))
			.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
			{
				var resolvedConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
				return new SocketsHttpHandler
				{
					ConnectCallback = async (_, cancellationToken) =>
					{
						var socketPath = resolvedConfiguration["DeploymentControl:SocketPath"]
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
				};
			});

		return services;
	}
}