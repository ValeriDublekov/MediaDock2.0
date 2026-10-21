using Testcontainers.PostgreSql;

namespace MediaDock.IntegrationTests;

internal static class PostgreSqlTestContainerBuilder
{
    public static PostgreSqlBuilder Create(string database)
    {
        return new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("mediadock")
            .WithPassword("mediadock_test")
            .WithPortBinding(5432, true)
            .WithCreateParameterModifier(parameters =>
            {
                var hostConfig = parameters.HostConfig
                    ?? throw new InvalidOperationException("PostgreSQL container host configuration was not created.");
                hostConfig.PortBindings.Values.Single().Single().HostIP = "127.0.0.1";
            });
    }
}