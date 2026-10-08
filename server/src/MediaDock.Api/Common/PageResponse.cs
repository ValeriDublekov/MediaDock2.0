using System.Text.Json.Serialization;

namespace MediaDock.Api.Common;

/// <summary>Represents a stable page of API results.</summary>
/// <typeparam name="T">The response item type.</typeparam>
public sealed record PageResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] YearBounds? YearBounds = null);

public sealed record YearBounds(int MinYear, int MaxYear);