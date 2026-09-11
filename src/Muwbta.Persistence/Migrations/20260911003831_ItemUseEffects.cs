using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Muwbta.Domain.Abilities;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemUseEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "use_cooldown_pulses",
                table: "item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<List<AbilityEffectSpec>>(
                name: "use_effects",
                table: "item_templates",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "use_cooldown_pulses",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "use_effects",
                table: "item_templates");
        }
    }
}
