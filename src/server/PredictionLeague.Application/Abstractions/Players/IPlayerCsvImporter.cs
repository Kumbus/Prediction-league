using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Players;

// CSV bulk-import port. PreviewAsync runs the full match logic without persisting;
// CommitAsync persists in a single transaction. Optional tournamentId binds resolved players
// to a TournamentSquad row (idempotent).
public interface IPlayerCsvImporter
{
    Task<PlayerImportPreview> PreviewAsync(Stream csv, Guid? tournamentId, CancellationToken cancellationToken = default);

    Task<PlayerImportResult> CommitAsync(Stream csv, Guid? tournamentId, CancellationToken cancellationToken = default);
}

public enum PlayerImportAction
{
    Create,
    Update,
    Skip
}

// Per-row preview entry. LineNumber is 1-based and includes the header row.
public record PlayerImportRow(
    int LineNumber,
    string Name,
    string NationalityCode,
    PlayerPosition Position,
    DateOnly? DateOfBirth,
    int? HeightCm,
    int? ExternalPlayerId,
    PlayerImportAction Action);

public record PlayerImportConflict(int LineNumber, string Name, string Reason);

public record PlayerImportPreview(
    int ToCreate,
    int ToUpdate,
    int Skipped,
    IReadOnlyList<PlayerImportRow> Rows,
    IReadOnlyList<PlayerImportConflict> Conflicts);

public record PlayerImportResult(int Created, int Updated, int Skipped, int SquadsAdded);
