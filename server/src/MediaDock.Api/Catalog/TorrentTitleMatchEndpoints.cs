using System.ComponentModel.DataAnnotations;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Catalog;

public sealed record ImdbCandidateResponse(string Title, int? Year, string? ImdbId, string SourceType);

public sealed record ImdbCandidateSearchResponse(
    string Status,
    IReadOnlyList<ImdbCandidateResponse> Candidates,
    int TotalResults,
    string? ErrorCode);

public sealed record ApplyImdbCandidateRequest(
    [Required, StringLength(32, MinimumLength = 9)] string ImdbId);

public sealed record AppliedImdbCandidateResponse(long TorrentTitleId, string ImdbId);

internal static class TorrentTitleMatchEndpoints
{
    public static WebApplication MapTorrentTitleMatchEndpoints(this WebApplication app)
    {
        app.MapPost("/api/titles/{id:long}/imdb-candidates", async Task<Ok<ImdbCandidateSearchResponse>> (
            long id,
            [Range(1, 100)] int? page,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.SearchCatalogTitleAsync(id, page ?? 1, cancellationToken));
        })
            .WithName("SearchCatalogTitleImdbCandidates")
            .WithSummary("Search OMDb candidates for a catalog title.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/titles/{id:long}/imdb-match", async Task<Ok<AppliedImdbCandidateResponse>> (
            long id,
            ApplyImdbCandidateRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.ApplyCatalogTitleMatchAsync(id, request.ImdbId, cancellationToken));
        })
            .WithName("ApplyCatalogTitleImdbCandidate")
            .WithSummary("Apply a confirmed IMDb candidate to a catalog title.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/parse-logs/{id:long}/imdb-candidates", async Task<Ok<ImdbCandidateSearchResponse>> (
            long id,
            [Range(1, 100)] int? page,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.SearchParseLogImdbCandidatesAsync(id, page ?? 1, cancellationToken));
        })
            .WithName("SearchParseLogImdbCandidates")
            .WithSummary("Search OMDb candidates for a parse-log title.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/parse-logs/{id:long}/imdb-match", async Task<Ok<AppliedImdbCandidateResponse>> (
            long id,
            ApplyImdbCandidateRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.ApplyParseLogMatchAsync(id, request.ImdbId, cancellationToken));
        })
            .WithName("ApplyParseLogImdbCandidate")
            .WithSummary("Apply a confirmed IMDb candidate to a parse-log torrent.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}