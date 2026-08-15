using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class PredictionConfiguration : IEntityTypeConfiguration<Prediction>
{
    public void Configure(EntityTypeBuilder<Prediction> builder)
    {
        builder.HasKey(p => p.Id);

        // FR-002 keys a forecast per (user, league, match) — this index *is* that key, and it is
        // also what makes a double-submit of the same round idempotent instead of duplicated.
        builder.HasIndex(p => new { p.LeagueId, p.UserId, p.MatchId }).IsUnique();

        // Deleting a league takes its forecasts; deleting a match does too. Cascade on both is
        // safe here because League.TournamentId carries no FK (LeagueConfiguration.cs:34), so
        // Tournament -> Match -> Prediction is the only cascading route in and the two paths share
        // no cascading ancestor. Restrict on the match FK would instead turn the unguarded
        // DELETE /api/matches/{id} into a 500.
        builder.HasOne<League>()
            .WithMany()
            .HasForeignKey(p => p.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Match>()
            .WithMany()
            .HasForeignKey(p => p.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // The scorer pair points at reference data that outlives any one prediction — restrict, so
        // deleting a player or a team can never silently rewrite a member's forecast.
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PredictedFirstScorerPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(p => p.PredictedFirstScorerTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // No HasDefaultValueSql on SubmittedUtc: the default on LeagueMembership.JoinedUtc exists
        // only to backfill rows that predate the column, and Predictions is a new table with no
        // such rows. Every insert stamps the value explicitly.

        // UserId is a bare Guid -> ApplicationUser.Id; no FK constraint, matching LeagueMembership.
    }
}
