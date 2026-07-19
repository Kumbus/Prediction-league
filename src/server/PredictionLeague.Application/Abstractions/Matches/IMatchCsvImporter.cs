using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Matches;

// Manual-match CSV bulk-import port. PreviewAsync runs the full parse/resolve pass without
// persisting; CommitAsync persists in a single transaction. Teams are resolved by name and
// auto-created (NULL ExternalTeamId) when missing — mirrors the ingest minimal-create rule.
public interface IMatchCsvImporter
{
    Task<MatchImportPreview> PreviewAsync(Guid tournamentId, Stream csv, CancellationToken cancellationToken = default);

    Task<MatchImportResult> CommitAsync(Guid tournamentId, Stream csv, CancellationToken cancellationToken = default);
}

// Per-row preview entry. LineNumber is 1-based and includes the header row.
public record MatchImportRow(
    int LineNumber,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    int? HomeScore,
    int? AwayScore,
    string Round);

public record MatchImportConflict(int LineNumber, string Reason);

public record MatchImportPreview(
    int ToCreate,
    int Skipped,
    int TeamsToCreate,
    IReadOnlyList<MatchImportRow> Rows,
    IReadOnlyList<MatchImportConflict> Conflicts);

public record MatchImportResult(int Created, int Skipped, int TeamsCreated);
