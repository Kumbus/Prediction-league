using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PredictionLeague.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterPlayerExternalIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_ExternalPlayerId",
                table: "Players");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ExternalPlayerId",
                table: "Players",
                column: "ExternalPlayerId",
                unique: true,
                filter: "[ExternalPlayerId] <> 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_ExternalPlayerId",
                table: "Players");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ExternalPlayerId",
                table: "Players",
                column: "ExternalPlayerId",
                unique: true);
        }
    }
}
