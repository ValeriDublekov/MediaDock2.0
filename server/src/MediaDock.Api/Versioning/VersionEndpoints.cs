using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace MediaDock.Api.Versioning;

internal static class VersionEndpoints
{
    public static WebApplication MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/version", (IConfiguration configuration) =>
        {
            var commitDate = configuration["MediaDock:CommitDateUtc"];
            DateTimeOffset? commitDateUtc = DateTimeOffset.TryParse(
                commitDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedCommitDate)
                ? parsedCommitDate
                : null;

            return TypedResults.Ok(new VersionResponse(
                configuration["MediaDock:Version"] ?? "local",
                configuration["MediaDock:CommitSha"] ?? "unknown",
                commitDateUtc));
        })
            .WithName("GetVersion")
            .WithSummary("Get the version of the running application.")
            .WithDescription("Returns build metadata embedded in the deployed application image.")
            .Produces<VersionResponse>(StatusCodes.Status200OK);

        return app;
    }
}

/// <summary>Identifies the source commit used to build the running application.</summary>
public sealed record VersionResponse(
    string Version,
    string CommitSha,
    DateTimeOffset? CommitDateUtc);