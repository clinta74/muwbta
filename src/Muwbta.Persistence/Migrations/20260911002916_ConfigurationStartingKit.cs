using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurationStartingKit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "starting_kit",
                table: "game_configurations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "starting_kit",
                table: "game_configurations");
        }
    }
}
