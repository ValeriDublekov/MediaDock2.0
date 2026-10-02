using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class DeploymentControlApiTests
{
    [Fact]
    public async Task StatusIsProxiedAndOnlyAllowlistedActionsReachTheHost()
    {
        var handler = new DeploymentControlHandler();
        using var factory = new DeploymentControlApiFactory(handler);
        using var client = factory.CreateClient();

        using var statusResponse = await client.GetAsync("/api/deployment");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<DeploymentStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal(new string('a', 40), status.DeployedSha);

        using var invalidResponse = await client.PostAsJsonAsync(
            "/api/deployment/run", new DeploymentActionRequest("run-arbitrary-command"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Null(handler.LastAction);

        using var retryResponse = await client.PostAsJsonAsync(
            "/api/deployment/run", new DeploymentActionRequest("retry_failed_gate"));
        Assert.Equal(HttpStatusCode.Accepted, retryResponse.StatusCode);
        Assert.Equal("retry_failed_gate", handler.LastAction);
    }

    private sealed class DeploymentControlApiFactory(DeploymentControlHandler handler) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DeploymentControlApiService>();
                services.AddSingleton(new DeploymentControlApiService(new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://mediadock-deploy-control")
                }));
            });
        }
    }

    private sealed class DeploymentControlHandler : HttpMessageHandler
    {
        public string? LastAction { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                var status = new DeploymentStatusResponse(
                    false,
                    "inactive",
                    "dead",
                    "success",
                    "0",
                    "started",
                    "finished",
                    new string('a', 40),
                    null,
                    false,
                    null,
                    ["Deployment succeeded."]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
            }

            var action = await request.Content!.ReadFromJsonAsync<DeploymentActionRequest>(cancellationToken);
            LastAction = action?.Action;
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new DeploymentActionResponse("Deployment check queued."))
            };
        }
    }
}