using System.Net;
using System.Net.Http.Json;

namespace MediaDock.Api.Operations;

public sealed class DeploymentControlApiService(HttpClient httpClient)
{
    public async Task<DeploymentStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/status", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeploymentStatusResponse>(cancellationToken)
            ?? throw new HttpRequestException("The deployment control returned an empty status.");
    }

    public async Task<(HttpStatusCode StatusCode, DeploymentActionResponse? Response)> StartAsync(
        string action,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/run", new DeploymentActionRequest(action), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DeploymentActionResponse>(cancellationToken);
        return (response.StatusCode, body);
    }
}

public sealed record DeploymentStatusResponse(
    bool IsRunning,
    string ActiveState,
    string SubState,
    string Result,
    string ExitCode,
    string StartedAt,
    string FinishedAt,
    string? DeployedSha,
    string? GateFailedSha,
    bool RecoveryRequired,
    string? FailureTargetSha,
    IReadOnlyList<string> RecentOutput);

public sealed record DeploymentActionRequest(string Action);

public sealed record DeploymentActionResponse(string Message);