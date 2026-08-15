using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PredictionLeague.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Predictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredictedHomeScore = table.Column<int>(type: "int", nullable: false),
                    PredictedAwayScore = table.Column<int>(type: "int", nullable: false),
                    PredictedFirstScorerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PredictedFirstScorerTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PredictedTotalCards = table.Column<int>(type: "int", nullable: true),
                    PredictedYellowCards = table.Column<int>(type: "int", nullable: true),
                    PredictedRedCards = table.Column<int>(type: "int", nullable: true),
                    SubmittedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AwardedPoints = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Predictions_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predictions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predictions_Players_PredictedFirstScorerPlayerId",
                        column: x => x.PredictedFirstScorerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Predictions_Teams_PredictedFirstScorerTeamId",
                        column: x => x.PredictedFirstScorerTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_LeagueId_UserId_MatchId",
                table: "Predictions",
                columns: new[] { "LeagueId", "UserId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_MatchId",
                table: "Predictions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_PredictedFirstScorerPlayerId",
                table: "Predictions",
                column: "PredictedFirstScorerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_PredictedFirstScorerTeamId",
                table: "Predictions",
                column: "PredictedFirstScorerTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Predictions");
        }
    }
}
