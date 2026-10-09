using System.ComponentModel.DataAnnotations;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Administration;

internal static class UserAdministrationEndpoints
{
    public static WebApplication MapUserAdministrationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/users", async Task<IResult> (
            [AsParameters] AdminUsersQuery query,
            UserAdministrationApiService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await service.GetUsersAsync(query, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: exception.Message);
            }
        })
            .WithName("GetAdminUsers")
            .WithSummary("List accounts and registration requests in the current open-access mode.")
            .Produces<AdminUsersPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/admin/registration-requests/{requestId:long}/decision", async Task<IResult> (
            long requestId,
            AdminRegistrationDecisionRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            UserAdministrationApiService service,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            var actor = await TryResolveAdministratorAsync(context, service, cancellationToken);

            var result = await service.DecideRegistrationRequestAsync(
                requestId,
                request.Decision,
                actor?.UserId,
                cancellationToken);
            return ToResult(result);
        })
            .WithName("DecideAdminRegistrationRequest")
            .WithSummary("Approve or reject a pending registration request.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPut("/api/admin/users/{userId:long}/status", async Task<IResult> (
            long userId,
            AdminUserStatusRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            UserAdministrationApiService service,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            var actor = await TryResolveAdministratorAsync(context, service, cancellationToken);

            var result = await service.SetUserStatusAsync(
                userId,
                request.Status,
                actor?.UserId,
                cancellationToken);
            return ToResult(result);
        })
            .WithName("SetAdminUserStatus")
            .WithSummary("Activate or deactivate a non-pending user account.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<AdministratorActor?> TryResolveAdministratorAsync(
        HttpContext context,
        UserAdministrationApiService service,
        CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && ValidatedGoogleIdentity.TryCreate(context.User, out var identity))
        {
            return await service.ResolveAdministratorAsync(identity!, cancellationToken);
        }

        return null;
    }

    private static IResult AdminRequiredProblem() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Active administrator access is required.");

    private static IResult ToResult(UserAdministrationResult result) => result switch
    {
        UserAdministrationResult.Succeeded => TypedResults.NoContent(),
        UserAdministrationResult.Forbidden => AdminRequiredProblem(),
        UserAdministrationResult.NotFound => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The requested account or registration request was not found."),
        UserAdministrationResult.Conflict => TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The requested account change is no longer valid."),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}

public sealed class AdminUsersQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [StringLength(200)]
    public string? Search { get; init; }

    public string? State { get; init; }
}

public sealed record AdminRegistrationDecisionRequest(
    [property: Required, RegularExpression("^(approve|reject)$")] string Decision);

public sealed record AdminUserStatusRequest(
    [property: Required, RegularExpression("^(active|deactivated)$")] string Status);