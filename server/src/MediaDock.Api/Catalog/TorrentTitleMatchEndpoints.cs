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

public sealed record ImdbCandidateSearchRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title);

public sealed record LookupImdbCandidateRequest(
    [Required, StringLength(32, MinimumLength = 9)] string ImdbId);

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
            ImdbCandidateSearchRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.SearchCatalogTitleAsync(id, request.Title, page ?? 1, cancellationToken));
        })
            .WithName("SearchCatalogTitleImdbCandidates")
            .WithSummary("Search OMDb candidates for a catalog title using an edited title.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/titles/{id:long}/imdb-candidate", async Task<Ok<ImdbCandidateSearchResponse>> (
            long id,
            LookupImdbCandidateRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.LookupCatalogTitleImdbCandidateAsync(id, request.ImdbId, cancellationToken));
        })
            .WithName("LookupCatalogTitleImdbCandidate")
            .WithSummary("Load a catalog title candidate by IMDb ID.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
            ImdbCandidateSearchRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.SearchParseLogImdbCandidatesAsync(id, request.Title, page ?? 1, cancellationToken));
        })
            .WithName("SearchParseLogImdbCandidates")
            .WithSummary("Search OMDb candidates for a parse-log title using an edited title.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/parse-logs/{id:long}/imdb-candidate", async Task<Ok<ImdbCandidateSearchResponse>> (
            long id,
            LookupImdbCandidateRequest request,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.LookupParseLogImdbCandidateAsync(id, request.ImdbId, cancellationToken));
        })
            .WithName("LookupParseLogImdbCandidate")
            .WithSummary("Load a parse-log candidate by IMDb ID.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

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