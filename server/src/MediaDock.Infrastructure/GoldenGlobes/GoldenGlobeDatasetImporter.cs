using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.GoldenGlobes;

public sealed record GoldenGlobeImportSummary(int RowsRead, int RowsSkippedByYear, int RowsSkippedByType, int RowsSkippedWithoutTitle, int AwardsCreated, int NominationsCreated);

internal sealed record GoldenGlobeRow(int Year, bool Winner, string Award, string Title, string NomineeType);

public sealed class GoldenGlobeDatasetImporter(MediaDockDbContext db)
{
    public async Task<GoldenGlobeImportSummary> ImportAsync(string path, int yearAfter = 1980, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Golden Globes dataset file was not found.", path);
        return await ImportAsync(await File.ReadAllBytesAsync(path, cancellationToken), yearAfter, cancellationToken);
    }

    public async Task<GoldenGlobeImportSummary> ImportAsync(byte[] content, int yearAfter = 1980, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var parser = new TextFieldParser(stream, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(Encoding.UTF8.GetString(content).Split(['\r', '\n'], 2)[0].Count(c => c == '\t') > Encoding.UTF8.GetString(content).Split(['\r', '\n'], 2)[0].Count(c => c == ',') ? "\t" : ",");
        var rawHeaders = parser.ReadFields() ?? throw new InvalidDataException("Golden Globes dataset has no header row.");
        var headers = rawHeaders.Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        var indexes = headers.Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "nominee_type", "year", "winner", "award", "title" })
            if (!indexes.ContainsKey(required)) throw new InvalidDataException($"Golden Globes dataset is missing required column: {required}.");

        var rows = new List<GoldenGlobeRow>(); var read = 0; var skippedYear = 0; var skippedType = 0; var skippedTitle = 0;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested(); var fields = parser.ReadFields(); if (fields is null) continue; read++;
            if (fields.Length != headers.Length) throw new InvalidDataException($"Golden Globes dataset row {parser.LineNumber} has an invalid field count.");
            string Field(string name) => fields[indexes[name]].Trim();
            if (!int.TryParse(Field("year"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) || year <= yearAfter) { skippedYear++; continue; }
            var type = Field("nominee_type").ToLowerInvariant();
            if (type is not ("tv-show" or "film" or "movie" or "series")) { skippedType++; continue; }
            var title = Field("title"); if (string.IsNullOrWhiteSpace(title)) { skippedTitle++; continue; }
            var nomineeType = type is "tv-show" or "series" ? "series" : "movie";
            rows.Add(new(year, ParseBool(Field("winner")), Field("award"), title, nomineeType));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var awardNames = rows.Select(x => x.Award).Distinct(StringComparer.Ordinal).ToArray();
        var awards = await db.GoldenGlobeAwards.Where(x => awardNames.Contains(x.Name)).ToDictionaryAsync(x => x.Name, StringComparer.Ordinal, cancellationToken);
        var categoryAliases = await db.AwardCategoryAliases
            .AsNoTracking()
            .Where(alias => alias.AwardSource == "golden_globes")
            .ToDictionaryAsync(alias => alias.RawName, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var keys = rows.Select(Key).Distinct().ToArray();
        var existing = await db.GoldenGlobeNominations
            .Where(x => keys.Contains(x.ImportKey))
            .Include(x => x.GoldenGlobeTitle)
            .ToDictionaryAsync(x => x.ImportKey, StringComparer.Ordinal, cancellationToken);
        var groupTitles = rows.Select(x => x.Title).Distinct(StringComparer.Ordinal).ToArray();
        var groupYears = rows.Select(x => x.Year).Distinct().ToArray();
        var groupTypes = rows.Select(x => x.NomineeType).Distinct(StringComparer.Ordinal).ToArray();
        var groups = await db.GoldenGlobeTitles
            .Where(x => groupTitles.Contains(x.Title) && groupYears.Contains(x.Year) && groupTypes.Contains(x.NomineeType))
            .ToDictionaryAsync(x => (x.Title, x.Year, x.NomineeType), cancellationToken);
        foreach (var nomination in existing.Values)
        {
            if (nomination.GoldenGlobeTitle is { } title)
                groups.TryAdd((title.Title, title.Year, title.NomineeType), title);
        }

        GoldenGlobeTitle GetGroup(GoldenGlobeRow row, int initialVersion = 0)
        {
            var identity = (row.Title, row.Year, row.NomineeType);
            if (groups.TryGetValue(identity, out var group)) return group;
            group = new GoldenGlobeTitle
            {
                Title = row.Title,
                Year = row.Year,
                NomineeType = row.NomineeType,
                ImdbIdVersion = initialVersion
            };
            db.GoldenGlobeTitles.Add(group);
            groups.Add(identity, group);
            return group;
        }

        var seenKeys = new HashSet<string>(existing.Keys, StringComparer.Ordinal);
        var displacedGroups = new HashSet<GoldenGlobeTitle>();
        var awardsCreated = 0; var nominationsCreated = 0;
        foreach (var row in rows)
        {
            var categoryKey = categoryAliases.TryGetValue(row.Award, out var categoryAlias)
                ? categoryAlias.CategoryKey
                : "award_other";
            if (!awards.TryGetValue(row.Award, out var award))
            {
                award = new GoldenGlobeAward { Name = row.Award, CategoryKey = categoryKey };
                db.GoldenGlobeAwards.Add(award);
                awards.Add(row.Award, award);
                awardsCreated++;
            }
            else if (award.CategoryKey != categoryKey)
            {
                award.CategoryKey = categoryKey;
            }

            var key = Key(row);
            if (!seenKeys.Add(key))
            {
                if (existing.TryGetValue(key, out var existingNomination)
                    && existingNomination.GoldenGlobeTitle is { } currentGroup
                    && currentGroup.NomineeType != row.NomineeType)
                {
                    var hasVerifiedAutomaticLink = !currentGroup.IsImdbIdManual
                        && currentGroup.EnrichmentStatus == "enriched"
                        && ImdbIdNormalizer.IsValid(currentGroup.ImdbId);
                    if (!hasVerifiedAutomaticLink)
                    {
                        existingNomination.GoldenGlobeTitle = GetGroup(row, checked(currentGroup.ImdbIdVersion + 1));
                        displacedGroups.Add(currentGroup);
                    }
                }

                continue;
            }

            db.GoldenGlobeNominations.Add(new GoldenGlobeNomination
            {
                ImportKey = key,
                Winner = row.Winner,
                Award = award,
                GoldenGlobeTitle = GetGroup(row)
            });
            nominationsCreated++;
        }
        // Keep the import responsive and avoid one very large EF change-detection pass.
        // Awards are shared tracked entities, so only nominations are flushed in batches.
        var pending = 0;
        foreach (var nomination in db.ChangeTracker.Entries<GoldenGlobeNomination>().Where(x => x.State == EntityState.Added).ToArray())
        {
            pending++;
            if (pending % 500 == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                foreach (var entry in db.ChangeTracker.Entries<GoldenGlobeNomination>().Where(x => x.State == EntityState.Unchanged).ToArray())
                    entry.State = EntityState.Detached;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var group in displacedGroups)
        {
            if (!await db.GoldenGlobeNominations.AnyAsync(x => x.GoldenGlobeTitleId == group.Id, cancellationToken))
                db.GoldenGlobeTitles.Remove(group);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(read, skippedYear, skippedType, skippedTitle, awardsCreated, nominationsCreated);
    }

    private static string Key(GoldenGlobeRow row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v1:{row.Year}\u001f{row.Winner}\u001f{row.Award}\u001f{row.Title}"))).ToLowerInvariant();
    private static bool ParseBool(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1";
}
