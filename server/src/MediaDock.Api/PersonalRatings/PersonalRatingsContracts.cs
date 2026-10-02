namespace MediaDock.Api.PersonalRatings;

public sealed class PersonalRatingsImportForm
{
    public IFormFile? File { get; set; }
}

public sealed record PersonalRatingsImportResponse(
    int RatingsInFile,
    int Added,
    int Updated,
    int Unchanged,
    int TotalRatings,
    DateTimeOffset ImportedAt,
    IReadOnlyList<PersonalRatingsImportError> Errors);

public sealed record PersonalRatingsImportError(string Id, string Message);