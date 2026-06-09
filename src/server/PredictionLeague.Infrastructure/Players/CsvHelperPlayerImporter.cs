using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Players;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Persistence;

namespace PredictionLeague.Infrastructure.Players;

// CsvHelper-backed importer. Parses rows, resolves nationality + existing-player match,
// classifies as Create / Update / Skip, optionally writes TournamentSquad rows. Commit runs
// inside a single SaveChangesAsync; ExternalPlayerId collisions and unknown NationalityCode
// land in Conflicts and are skipped from commit.
public class CsvHelperPlayerImporter : IPlayerCsvImporter
{
    private readonly INationalityRepository _nationalities;
    private readonly IPlayerRepository _players;
    private readonly ITournamentSquadRepository _squads;
    private readonly AppDbContext _context;

    public CsvHelperPlayerImporter(
        INationalityRepository nationalities,
        IPlayerRepository players,
        ITournamentSquadRepository squads,
        AppDbContext context)
    {
        _nationalities = nationalities;
        _players = players;
        _squads = squads;
        _context = context;
    }

    public async Task<PlayerImportPreview> PreviewAsync(Stream csv, Guid? tournamentId, CancellationToken cancellationToken = default)
    {
        var (rows, conflicts, _) = await ResolveAsync(csv, tournamentId, persist: false, cancellationToken);
        return new PlayerImportPreview(
            rows.Count(r => r.Action == PlayerImportAction.Create),
            rows.Count(r => r.Action == PlayerImportAction.Update),
            conflicts.Count,
            rows,
            conflicts);
    }

    public async Task<PlayerImportResult> CommitAsync(Stream csv, Guid? tournamentId, CancellationToken cancellationToken = default)
    {
        var (rows, conflicts, squadsAdded) = await ResolveAsync(csv, tournamentId, persist: true, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlayerImportResult(
            rows.Count(r => r.Action == PlayerImportAction.Create),
            rows.Count(r => r.Action == PlayerImportAction.Update),
            conflicts.Count,
            squadsAdded);
    }

    private async Task<(List<PlayerImportRow> Rows, List<PlayerImportConflict> Conflicts, int SquadsAdded)> ResolveAsync(
        Stream csv, Guid? tournamentId, bool persist, CancellationToken cancellationToken)
    {
        var nats = await _nationalities.ListAsync(cancellationToken);
        var natByCode = nats.ToDictionary(n => n.Code, n => n, StringComparer.OrdinalIgnoreCase);

        var rows = new List<PlayerImportRow>();
        var conflicts = new List<PlayerImportConflict>();
        int squadsAdded = 0;

        var parsed = ParseCsv(csv);

        for (int i = 0; i < parsed.Count; i++)
        {
            var raw = parsed[i];
            var lineNumber = i + 2; // header row = line 1

            if (string.IsNullOrWhiteSpace(raw.Name) || string.IsNullOrWhiteSpace(raw.NationalityCode))
            {
                conflicts.Add(new PlayerImportConflict(lineNumber, raw.Name ?? string.Empty, "Name and NationalityCode are required."));
                continue;
            }

            if (!natByCode.TryGetValue(raw.NationalityCode, out var nationality))
            {
                conflicts.Add(new PlayerImportConflict(lineNumber, raw.Name, $"Unknown NationalityCode '{raw.NationalityCode}'."));
                continue;
            }

            var position = ParsePosition(raw.Position);
            var dob = ParseDate(raw.DateOfBirth);
            var height = ParseInt(raw.HeightCm);
            var ext = ParseInt(raw.ExternalPlayerId);

            var existing = await _players.FindByNameAndNationalityAsync(raw.Name, nationality.Id, cancellationToken);

            if (ext.HasValue)
            {
                var collidingOwner = await _players.GetByExternalPlayerIdAsync(ext.Value, cancellationToken);
                if (collidingOwner is not null && (existing is null || collidingOwner.Id != existing.Id))
                {
                    conflicts.Add(new PlayerImportConflict(
                        lineNumber, raw.Name,
                        $"ExternalPlayerId {ext.Value} is already owned by a different player."));
                    continue;
                }
            }

            PlayerImportAction action;
            Player target;
            if (existing is null)
            {
                target = new Player
                {
                    Id = Guid.NewGuid(),
                    Name = raw.Name,
                    NationalityId = nationality.Id,
                    Position = position ?? PlayerPosition.Unknown,
                    DateOfBirth = dob,
                    HeightCm = height,
                    ExternalPlayerId = ext ?? 0
                };
                action = PlayerImportAction.Create;
                if (persist) await _players.AddAsync(target, cancellationToken);
            }
            else
            {
                target = existing;
                // Partial-update semantics: don't blank existing values with empty CSV cells.
                if (position.HasValue) target.Position = position.Value;
                if (dob.HasValue) target.DateOfBirth = dob;
                if (height.HasValue) target.HeightCm = height;
                if (ext.HasValue) target.ExternalPlayerId = ext.Value;
                action = PlayerImportAction.Update;
                if (persist) _players.Update(target);
            }

            rows.Add(new PlayerImportRow(
                lineNumber, raw.Name, nationality.Code,
                position ?? PlayerPosition.Unknown,
                dob, height, ext, action));

            if (persist && tournamentId.HasValue)
            {
                var exists = await _squads.ExistsAsync(tournamentId.Value, target.Id, cancellationToken);
                if (!exists)
                {
                    await _squads.AddAsync(
                        new TournamentSquad { TournamentId = tournamentId.Value, PlayerId = target.Id },
                        cancellationToken);
                    squadsAdded++;
                }
            }
        }

        return (rows, conflicts, squadsAdded);
    }

    private static List<PlayerCsvRow> ParseCsv(Stream csv)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var reader = new StreamReader(csv, leaveOpen: true);
        using var parser = new CsvReader(reader, config);
        return parser.GetRecords<PlayerCsvRow>().ToList();
    }

    private static PlayerPosition? ParsePosition(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<PlayerPosition>(raw, ignoreCase: true, out var p) ? p : PlayerPosition.Unknown;
    }

    private static DateOnly? ParseDate(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null :
            DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null :
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private sealed class PlayerCsvRow
    {
        [Name("Name")] public string Name { get; set; } = string.Empty;
        [Name("NationalityCode")] public string NationalityCode { get; set; } = string.Empty;
        [Name("Position"), Optional] public string? Position { get; set; }
        [Name("DateOfBirth"), Optional] public string? DateOfBirth { get; set; }
        [Name("HeightCm"), Optional] public string? HeightCm { get; set; }
        [Name("ExternalPlayerId"), Optional] public string? ExternalPlayerId { get; set; }
    }
}
