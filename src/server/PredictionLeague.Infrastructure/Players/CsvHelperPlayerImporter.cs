using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using PredictionLeague.Application.Abstractions;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Players;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Persistence;

namespace PredictionLeague.Infrastructure.Players;

// CsvHelper-backed importer. Parses rows, resolves nationality + club/national team + existing-
// player match, classifies as Create / Update / Skip, optionally writes TournamentSquad rows.
// Commit runs inside a single SaveChangesAsync; ExternalPlayerId collisions, unknown
// NationalityCode and unknown team names land in Conflicts and are skipped from commit.
public class CsvHelperPlayerImporter : IPlayerCsvImporter
{
    private readonly INationalityRepository _nationalities;
    private readonly IPlayerRepository _players;
    private readonly ITournamentSquadRepository _squads;
    private readonly ITeamRepository _teams;
    private readonly AppDbContext _context;

    public CsvHelperPlayerImporter(
        INationalityRepository nationalities,
        IPlayerRepository players,
        ITournamentSquadRepository squads,
        ITeamRepository teams,
        AppDbContext context)
    {
        _nationalities = nationalities;
        _players = players;
        _squads = squads;
        _teams = teams;
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

        // Pre-load once instead of 2-3 round-trips per row: existing players keyed by their two
        // natural keys, plus the tournament's current squad. Dictionaries stay live — a player
        // created earlier in this file is found by later rows, so duplicate rows update in place.
        var allPlayers = await _players.GetAllAsync(cancellationToken);
        var byNameNat = new Dictionary<string, Player>();
        foreach (var p in allPlayers)
            byNameNat.TryAdd(NameNatKey(p.Name, p.NationalityId), p);
        var byExtId = new Dictionary<int, Player>();
        foreach (var p in allPlayers.Where(p => p.ExternalPlayerId != 0))
            byExtId.TryAdd(p.ExternalPlayerId, p);

        // Teams are looked up by name, never created: the match importer auto-creates a missing
        // team because a fixture is meaningless without both sides, but a typo in a player row
        // would silently mint a junk team that then shows up in every admin team picker.
        var teamsByName = (await _teams.ListAsync(cancellationToken))
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var squadPlayerIds = tournamentId.HasValue
            ? (await _squads.ListByTournamentAsync(tournamentId.Value, cancellationToken)).Select(s => s.PlayerId).ToHashSet()
            : new HashSet<Guid>();

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

            // Both team columns are optional; a blank cell leaves the existing link untouched,
            // matching the importer's other optional columns. A named team that does not exist is
            // a row conflict — resolved before anything is written so the row is skipped whole.
            if (!TryResolveTeam(raw.ClubTeam, teamsByName, out var clubTeam))
            {
                conflicts.Add(new PlayerImportConflict(lineNumber, raw.Name, $"Unknown ClubTeam '{raw.ClubTeam!.Trim()}'."));
                continue;
            }
            if (!TryResolveTeam(raw.NationalTeam, teamsByName, out var nationalTeam))
            {
                conflicts.Add(new PlayerImportConflict(lineNumber, raw.Name, $"Unknown NationalTeam '{raw.NationalTeam!.Trim()}'."));
                continue;
            }

            var position = ParsePosition(raw.Position);
            var dob = ParseDate(raw.DateOfBirth);
            var height = ParseInt(raw.HeightCm);
            var ext = ParseInt(raw.ExternalPlayerId);

            byNameNat.TryGetValue(NameNatKey(raw.Name, nationality.Id), out var existing);

            if (ext.HasValue && ext.Value != 0)
            {
                var collidingOwner = byExtId.GetValueOrDefault(ext.Value);
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
                    ExternalPlayerId = ext ?? 0,
                    ClubTeamId = clubTeam?.Id,
                    NationalTeamId = nationalTeam?.Id
                };
                action = PlayerImportAction.Create;
                if (persist) await _players.AddAsync(target, cancellationToken);
                byNameNat[NameNatKey(target.Name, target.NationalityId)] = target;
                if (target.ExternalPlayerId != 0) byExtId[target.ExternalPlayerId] = target;
            }
            else
            {
                target = existing;
                // Partial-update semantics: don't blank existing values with empty CSV cells.
                if (position.HasValue) target.Position = position.Value;
                if (dob.HasValue) target.DateOfBirth = dob;
                if (height.HasValue) target.HeightCm = height;
                if (ext.HasValue) target.ExternalPlayerId = ext.Value;
                if (clubTeam is not null) target.ClubTeamId = clubTeam.Id;
                if (nationalTeam is not null) target.NationalTeamId = nationalTeam.Id;
                action = PlayerImportAction.Update;
                if (persist) _players.Update(target);
                if (target.ExternalPlayerId != 0) byExtId[target.ExternalPlayerId] = target;
            }

            rows.Add(new PlayerImportRow(
                lineNumber, raw.Name, nationality.Code,
                position ?? PlayerPosition.Unknown,
                dob, height, ext, action));

            if (tournamentId.HasValue && squadPlayerIds.Add(target.Id))
            {
                squadsAdded++;
                if (persist)
                    await _squads.AddAsync(
                        new TournamentSquad { TournamentId = tournamentId.Value, PlayerId = target.Id },
                        cancellationToken);
            }
        }

        return (rows, conflicts, squadsAdded);
    }

    // Blank cell -> (true, null): "leave it alone". Named but unknown -> false, so the caller
    // reports a conflict. The two outcomes are deliberately distinct: silently ignoring a typo
    // would leave the player unlinked and the scorer picker empty with nothing to explain it.
    private static bool TryResolveTeam(string? raw, Dictionary<string, Team> teamsByName, out Team? team)
    {
        team = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return teamsByName.TryGetValue(raw.Trim(), out team);
    }

    // Natural upsert key for a player: name (case-insensitive) + nationality.
    private static string NameNatKey(string name, int? nationalityId)
        => $"{name.Trim().ToLowerInvariant()}|{nationalityId}";

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
        try
        {
            return parser.GetRecords<PlayerCsvRow>().ToList();
        }
        catch (Exception ex) when (ex is CsvHelperException or IOException)
        {
            throw new CsvImportException("The CSV file could not be parsed. Check the headers and formatting.", ex);
        }
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
        [Name("ClubTeam"), Optional] public string? ClubTeam { get; set; }
        [Name("NationalTeam"), Optional] public string? NationalTeam { get; set; }
    }
}
