using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using PredictionLeague.Application.Abstractions;
using PredictionLeague.Application.Abstractions.Matches;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Persistence;

namespace PredictionLeague.Infrastructure.Matches;

// CsvHelper-backed manual-match importer. Resolves home/away teams by name (case-insensitive),
// auto-creating missing ones with a NULL ExternalTeamId. Every valid row is a Create — manual
// matches carry no external key, so re-uploading the same file re-creates rows. Commit runs
// inside a single SaveChangesAsync; malformed rows land in Conflicts and are skipped.
public class CsvHelperMatchImporter : IMatchCsvImporter
{
    private readonly ITeamRepository _teams;
    private readonly IMatchRepository _matches;
    private readonly ITournamentRepository _tournaments;
    private readonly AppDbContext _context;

    public CsvHelperMatchImporter(
        ITeamRepository teams,
        IMatchRepository matches,
        ITournamentRepository tournaments,
        AppDbContext context)
    {
        _teams = teams;
        _matches = matches;
        _tournaments = tournaments;
        _context = context;
    }

    public async Task<MatchImportPreview> PreviewAsync(Guid tournamentId, Stream csv, CancellationToken cancellationToken = default)
    {
        var r = await ResolveAsync(tournamentId, csv, persist: false, cancellationToken);
        return new MatchImportPreview(r.Rows.Count, r.Conflicts.Count, r.TeamsCreated, r.Rows, r.Conflicts);
    }

    public async Task<MatchImportResult> CommitAsync(Guid tournamentId, Stream csv, CancellationToken cancellationToken = default)
    {
        var r = await ResolveAsync(tournamentId, csv, persist: true, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new MatchImportResult(r.Rows.Count, r.Conflicts.Count, r.TeamsCreated);
    }

    private async Task<(List<MatchImportRow> Rows, List<MatchImportConflict> Conflicts, int TeamsCreated)> ResolveAsync(
        Guid tournamentId, Stream csv, bool persist, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId, cancellationToken)
            ?? throw new InvalidOperationException("Tournament not found.");

        var teamsByName = (await _teams.ListAsync(cancellationToken))
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        // Manual matches have no external key, so dedup on (home, away, kickoff): load the
        // tournament's existing fixtures once and reject rows that collide — with the DB and
        // with earlier rows in the same file.
        var seen = (await _matches.ListByTournamentAsync(tournamentId, cancellationToken))
            .Select(m => MatchKey(m.HomeTeam.Id, m.AwayTeam.Id, m.KickoffUtc))
            .ToHashSet();

        var rows = new List<MatchImportRow>();
        var conflicts = new List<MatchImportConflict>();
        var newTeams = new List<Team>();

        var parsed = ParseCsv(csv);

        for (int i = 0; i < parsed.Count; i++)
        {
            var raw = parsed[i];
            var lineNumber = i + 2; // header row = line 1

            var home = raw.HomeTeam?.Trim();
            var away = raw.AwayTeam?.Trim();

            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "HomeTeam and AwayTeam are required."));
                continue;
            }
            if (string.Equals(home, away, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "Home and away teams must differ."));
                continue;
            }

            if (!TryParseKickoff(raw.KickoffUtc, out var kickoff))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, $"Unparseable KickoffUtc '{raw.KickoffUtc}'."));
                continue;
            }

            var status = ParseStatus(raw.Status);
            var homeScore = ParseInt(raw.HomeScore);
            var awayScore = ParseInt(raw.AwayScore);

            if (status == MatchStatus.Finished && (homeScore is null || awayScore is null))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "A finished match needs both scores."));
                continue;
            }
            if (homeScore is < 0 || awayScore is < 0)
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "Scores cannot be negative."));
                continue;
            }

            // Round is the unit the predictions screen reads, writes and navigates by, so a blank
            // one is a conflict rather than a "Manual" placeholder that would collapse every match
            // in the tournament into one round. Checked before teams are resolved and before the
            // dedup key is claimed, so a rejected row stages nothing.
            if (string.IsNullOrWhiteSpace(raw.Round))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "Round is required."));
                continue;
            }
            var round = raw.Round.Trim();

            var homeTeam = ResolveTeam(home, teamsByName, newTeams);
            var awayTeam = ResolveTeam(away, teamsByName, newTeams);

            var key = MatchKey(homeTeam.Id, awayTeam.Id, kickoff);
            if (!seen.Add(key))
            {
                conflicts.Add(new MatchImportConflict(lineNumber, "Duplicate of an existing/earlier match (same teams and kickoff)."));
                continue;
            }

            if (persist)
            {
                await _matches.AddAsync(new Match
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    ExternalFixtureId = null,
                    Season = tournament.Season,
                    Round = round,
                    HomeTeamId = homeTeam.Id,
                    AwayTeamId = awayTeam.Id,
                    KickoffUtc = kickoff,
                    Status = status,
                    HomeScore = homeScore,
                    AwayScore = awayScore
                }, cancellationToken);
            }

            rows.Add(new MatchImportRow(
                lineNumber, homeTeam.Name, awayTeam.Name, kickoff, status, homeScore, awayScore, round));
        }

        // Persist newly-referenced teams in the same unit of work as the matches; app-generated
        // Guid keys mean insert order relative to the matches doesn't matter.
        if (persist)
            foreach (var t in newTeams)
                await _teams.AddAsync(t, cancellationToken);

        return (rows, conflicts, newTeams.Count);
    }

    // Returns the existing/newly-created team for a name. New teams are cached in the same
    // dictionary (so a name referenced by several rows is created once) and staged in newTeams.
    private static Team ResolveTeam(string name, Dictionary<string, Team> teamsByName, List<Team> newTeams)
    {
        if (teamsByName.TryGetValue(name, out var existing)) return existing;

        var team = new Team { Id = Guid.NewGuid(), Name = name, ExternalTeamId = null };
        teamsByName[name] = team;
        newTeams.Add(team);
        return team;
    }

    // Dedup key: same opponents at the same instant. UnixSeconds normalizes any offset diff.
    private static string MatchKey(Guid homeId, Guid awayId, DateTimeOffset kickoff)
        => $"{homeId}|{awayId}|{kickoff.ToUnixTimeSeconds()}";

    private static List<MatchCsvRow> ParseCsv(Stream csv)
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
            return parser.GetRecords<MatchCsvRow>().ToList();
        }
        catch (Exception ex) when (ex is CsvHelperException or IOException)
        {
            throw new CsvImportException("The CSV file could not be parsed. Check the headers and formatting.", ex);
        }
    }

    private static bool TryParseKickoff(string? raw, out DateTimeOffset kickoff)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out kickoff))
            return true;
        kickoff = default;
        return false;
    }

    private static MatchStatus ParseStatus(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && Enum.TryParse<MatchStatus>(raw, ignoreCase: true, out var s)
            ? s : MatchStatus.Scheduled;

    private static int? ParseInt(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null :
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private sealed class MatchCsvRow
    {
        [Name("HomeTeam")] public string HomeTeam { get; set; } = string.Empty;
        [Name("AwayTeam")] public string AwayTeam { get; set; } = string.Empty;
        [Name("KickoffUtc")] public string KickoffUtc { get; set; } = string.Empty;
        [Name("Status"), Optional] public string? Status { get; set; }
        [Name("HomeScore"), Optional] public string? HomeScore { get; set; }
        [Name("AwayScore"), Optional] public string? AwayScore { get; set; }
        [Name("Round"), Optional] public string? Round { get; set; }
    }
}
