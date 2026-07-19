using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PredictionLeague.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualMatchEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Teams_ExternalTeamId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Matches_ExternalFixtureId",
                table: "Matches");

            migrationBuilder.AlterColumn<int>(
                name: "ExternalTeamId",
                table: "Teams",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ExternalFixtureId",
                table: "Matches",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ExternalTeamId",
                table: "Teams",
                column: "ExternalTeamId",
                unique: true,
                filter: "[ExternalTeamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ExternalFixtureId",
                table: "Matches",
                column: "ExternalFixtureId",
                unique: true,
                filter: "[ExternalFixtureId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Teams_ExternalTeamId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Matches_ExternalFixtureId",
                table: "Matches");

            migrationBuilder.AlterColumn<int>(
                name: "ExternalTeamId",
                table: "Teams",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ExternalFixtureId",
                table: "Matches",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ExternalTeamId",
                table: "Teams",
                column: "ExternalTeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ExternalFixtureId",
                table: "Matches",
                column: "ExternalFixtureId",
                unique: true);
        }
    }
}
