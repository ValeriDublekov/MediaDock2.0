using System.Security.Cryptography;
using System.Text;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Infrastructure.OscarAwards;

public sealed record OscarImportSummary(
    int RowsRead,
    int RowsSkippedByYear,
    int RowsSkippedByCategory,
    int RowsSkippedWithoutFilm,
    int OscarMoviesCreated,
    int NominationsCreated,
    int NominationsUpdated);

public sealed class OscarDatasetImporter(MediaDockDbContext dbContext)
{
    public async Task<OscarImportSummary> ImportAsync(
        string path,
        int yearAfter = 1980,
        CancellationToken cancellationToken = default)
        => await ImportAsync(OscarCsvDatasetReader.Read(path, yearAfter, cancellationToken), cancellationToken);

    public async Task<OscarImportSummary> ImportAsync(
        byte[] content,
        int yearAfter = 1980,
        CancellationToken cancellationToken = default)
        => await ImportAsync(OscarCsvDatasetReader.Read(content, yearAfter, cancellationToken), cancellationToken);

    private async Task<OscarImportSummary> ImportAsync(
        OscarCsvReadResult dataset,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ImportOnceAsync(dataset, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsImdbIdentityUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await ImportOnceAsync(dataset, cancellationToken);
        }
    }

    private async Task<OscarImportSummary> ImportOnceAsync(
        OscarCsvReadResult dataset,
        CancellationToken cancellationToken)
    {
        if (dataset.Rows.Count == 0)
        {
            return CreateSummary(dataset, 0, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        var preparedRows = dataset.Rows
            .Select(row => new PreparedOscarRow(
                row,
                NormalizeTitle(row.FilmTitle),
                CreateFilmStableKey(row, NormalizeTitle(row.FilmTitle))))
            .Select(prepared => prepared with
            {
                ImportKey = CreateNominationImportKey(prepared.Row, prepared.FilmStableKey)
            })
            .ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var filmKeys = preparedRows
                .SelectMany(row => new[]
                {
                    row.FilmStableKey,
                    CreateTitleFilmStableKey(row.NormalizedTitle, row.Row.FilmYear)
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var nominationKeys = preparedRows
                .SelectMany(row => new[]
                {
                    row.ImportKey,
                    CreateNominationImportKey(
                        row.Row,
                        CreateTitleFilmStableKey(row.NormalizedTitle, row.Row.FilmYear))
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            await ImdbTitleMetadataMapper.EnsureAsync(
                dbContext, preparedRows.Select(row => row.Row.ImdbId), now, cancellationToken);
            var oscarMovies = await dbContext.OscarMovies
                .Where(film => filmKeys.Contains(film.StableKey))
                .ToListAsync(cancellationToken);
            var moviesByStableKey = oscarMovies.ToDictionary(movie => movie.StableKey, StringComparer.Ordinal);
            var nominations = await dbContext.OscarNominations
                .Where(nomination => nominationKeys.Contains(nomination.ImportKey))
                .Include(nomination => nomination.OscarMovie)
                .ToListAsync(cancellationToken);
            var nominationsByImportKey = nominations.ToDictionary(
                nomination => nomination.ImportKey,
                StringComparer.Ordinal);

            foreach (var movie in oscarMovies)
            {
                movie.ImdbId = ImdbIdNormalizer.Normalize(movie.ImdbId);
            }

            var oscarMoviesCreated = 0;
            var nominationsCreated = 0;
            var nominationsUpdated = 0;
            foreach (var prepared in preparedRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = prepared.Row;
                moviesByStableKey.TryGetValue(prepared.FilmStableKey, out var oscarMovie);
                if (oscarMovie is null)
                {
                    var titleStableKey = CreateTitleFilmStableKey(prepared.NormalizedTitle, row.FilmYear);
                    if (moviesByStableKey.TryGetValue(titleStableKey, out var titleKeyedMovie)
                        && ImdbIdNormalizer.IsCompatible(titleKeyedMovie.ImdbId, row.ImdbId))
                    {
                        oscarMovie = titleKeyedMovie;
                    }
                }

                if (oscarMovie is null)
                {
                    oscarMovie = new OscarMovie
                    {
                        StableKey = prepared.FilmStableKey,
                        FilmTitle = row.FilmTitle,
                        NormalizedTitle = prepared.NormalizedTitle,
                        FilmYear = row.FilmYear,
                        ImdbId = ImdbIdNormalizer.Normalize(row.ImdbId),
                        EnrichmentStatus = "pending",
                        ImportedAt = now,
                        UpdatedAt = now
                    };
                    dbContext.OscarMovies.Add(oscarMovie);
                    moviesByStableKey.Add(prepared.FilmStableKey, oscarMovie);
                    oscarMoviesCreated++;
                }

                ApplyMovieSourceData(oscarMovie, row, prepared.NormalizedTitle, now);
                var nominationImportKey = CreateNominationImportKey(row, oscarMovie.StableKey);
                if (!nominationsByImportKey.TryGetValue(nominationImportKey, out var nomination))
                {
                    nomination = new OscarNomination
                    {
                        ImportKey = nominationImportKey,
                        ImportedAt = now,
                        UpdatedAt = now
                    };
                    dbContext.OscarNominations.Add(nomination);
                    nominationsByImportKey.Add(nominationImportKey, nomination);
                    ApplyNominationSourceData(nomination, row, oscarMovie, now);
                    nominationsCreated++;
                }
                else if (ApplyNominationSourceData(nomination, row, oscarMovie, now))
                {
                    nominationsUpdated++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreateSummary(dataset, oscarMoviesCreated, nominationsCreated, nominationsUpdated);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static OscarImportSummary CreateSummary(
        OscarCsvReadResult dataset,
        int oscarMoviesCreated,
        int nominationsCreated,
        int nominationsUpdated) =>
        new(
            dataset.RowsRead,
            dataset.RowsSkippedByYear,
            dataset.RowsSkippedByCategory,
            dataset.RowsSkippedWithoutFilm,
            oscarMoviesCreated,
            nominationsCreated,
            nominationsUpdated);

    private static void ApplyMovieSourceData(
        OscarMovie movie,
        OscarDatasetRow row,
        string normalizedTitle,
        DateTimeOffset now)
    {
        var movieImdbId = ImdbIdNormalizer.Normalize(movie.ImdbId);
        var incomingImdbId = ImdbIdNormalizer.Normalize(row.ImdbId);
        if (!ImdbIdNormalizer.IsCompatible(movieImdbId, incomingImdbId))
        {
            throw new InvalidOperationException("Oscar movie IMDb identity conflicts with its imported source ID.");
        }

        movie.FilmTitle = row.FilmTitle;
        movie.NormalizedTitle = normalizedTitle;
        movie.FilmYear = row.FilmYear;
        movie.ImdbId = incomingImdbId ?? movieImdbId;
        if (movie.ImportedAt == default)
        {
            movie.ImportedAt = now;
        }

        movie.UpdatedAt = now;
    }

    private static bool ApplyNominationSourceData(
        OscarNomination nomination,
        OscarDatasetRow row,
        OscarMovie movie,
        DateTimeOffset now)
    {
        var changed = false;
        changed |= SetIfDifferent(nomination.Ceremony, row.Ceremony, value => nomination.Ceremony = value);
        changed |= SetIfDifferent(nomination.Class, row.Class, value => nomination.Class = value);
        changed |= SetIfDifferent(nomination.CanonicalCategory, row.CanonicalCategory, value => nomination.CanonicalCategory = value);
        changed |= SetIfDifferent(nomination.Category, row.Category, value => nomination.Category = value);
        changed |= SetIfDifferent(nomination.Name, row.Name, value => nomination.Name = value);
        changed |= SetIfDifferent(nomination.Nominees, row.Nominees, value => nomination.Nominees = value);
        changed |= SetIfDifferent(nomination.NomineeIds, row.NomineeIds, value => nomination.NomineeIds = value);
        changed |= SetIfDifferent(nomination.Detail, row.Detail, value => nomination.Detail = value);
        changed |= SetIfDifferent(nomination.IsWinner, row.IsWinner, value => nomination.IsWinner = value);
        if (movie.Id != 0)
        {
            changed |= SetIfDifferent(nomination.OscarMovieId, movie.Id, value => nomination.OscarMovieId = value);
        }
        if (nomination.OscarMovie != movie)
        {
            nomination.OscarMovie = movie;
            changed = true;
        }

        if (nomination.ImportedAt == default)
        {
            nomination.ImportedAt = now;
        }

        if (nomination.UpdatedAt == default || changed)
        {
            nomination.UpdatedAt = now;
        }

        return changed;
    }

    private static bool SetIfDifferent<T>(T current, T value, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        setter(value);
        return true;
    }

    private static string CreateFilmStableKey(OscarDatasetRow row, string normalizedTitle) =>
        row.ImdbId is not null
            ? $"imdb:{row.ImdbId.ToLowerInvariant()}"
            : CreateTitleFilmStableKey(normalizedTitle, row.FilmYear);

    private static string CreateTitleFilmStableKey(string normalizedTitle, int year) =>
        $"title:{CreateTitleIdentity(normalizedTitle, year)}";

    private static string CreateNominationImportKey(OscarDatasetRow row, string filmStableKey)
    {
        var nomineeIdentity = string.IsNullOrWhiteSpace(row.NomineeIds) ? row.Name : row.NomineeIds;
        var identity = string.Join('\u001f',
            row.Ceremony.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.CanonicalCategory.Trim().ToUpperInvariant(),
            filmStableKey,
            NormalizeIdentity(nomineeIdentity),
            NormalizeIdentity(row.Nominees),
            NormalizeIdentity(row.Detail));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v1:{identity}"))).ToLowerInvariant();
    }

    private static string CreateTitleIdentity(string normalizedTitle, int year) =>
        $"{normalizedTitle}|{year.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string NormalizeTitle(string value) => NormalizeIdentity(value).ToLowerInvariant();

    private static string NormalizeIdentity(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsImdbIdentityUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_oscar_movies_stable_key"
        };

    private sealed record PreparedOscarRow(OscarDatasetRow Row, string NormalizedTitle, string FilmStableKey)
    {
        public string ImportKey { get; init; } = string.Empty;
    }
}