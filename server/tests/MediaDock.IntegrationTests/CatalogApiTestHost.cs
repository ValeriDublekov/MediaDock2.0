using System.Net.Http;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace MediaDock.IntegrationTests;

internal sealed class CatalogApiTestHost : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly ApiFactory _factory;

    private CatalogApiTestHost(
        PostgreSqlContainer postgres,
        MediaDockDbContext db,
        ApiFactory factory,
        HttpClient client)
    {
        _postgres = postgres;
        Db = db;
        _factory = factory;
        Client = client;
    }

    internal MediaDockDbContext Db { get; }
    internal HttpClient Client { get; }

    internal static async Task<CatalogApiTestHost> CreateAsync(
        string database,
        IReadOnlyDictionary<string, string?>? buildMetadata = null,
        IOmdbClientFactory? omdbClientFactory = null)
    {
        var postgres = PostgreSqlTestContainerBuilder.Create(database).Build();
        MediaDockDbContext? db = null;
        ApiFactory? factory = null;
        try
        {
            await postgres.StartAsync();
            var dbOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .Options;
            db = new MediaDockDbContext(dbOptions);
            await db.Database.MigrateAsync();

            factory = new ApiFactory(postgres.GetConnectionString(), buildMetadata, omdbClientFactory);
            var client = factory.CreateClient();
            return new CatalogApiTestHost(postgres, db, factory, client);
        }
        catch
        {
            factory?.Dispose();
            if (db is not null)
            {
                await db.DisposeAsync();
            }

            await postgres.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _factory.Dispose();
        await Db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private sealed class ApiFactory(
        string connectionString,
        IReadOnlyDictionary<string, string?>? buildMetadata,
        IOmdbClientFactory? omdbClientFactory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                };
                if (buildMetadata is not null)
                {
                    foreach (var (key, value) in buildMetadata)
                    {
                        values[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(values);
            });

            if (omdbClientFactory is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IOmdbClientFactory>();
                    services.AddSingleton(omdbClientFactory);
                });
            }
        }
    }
}