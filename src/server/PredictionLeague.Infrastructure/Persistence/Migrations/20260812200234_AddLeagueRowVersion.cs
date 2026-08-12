using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PredictionLeague.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Leagues",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Leagues");
        }
    }
}
