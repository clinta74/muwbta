using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModerationPolicy : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// <b>The table is created before the column is dropped, and the list is carried across.</b>
        /// EF scaffolded the drop first, which would have thrown away whatever a server had been
        /// refusing to hear - silently, and noticed only the next time somebody said one of the
        /// words.
        /// <para>
        /// Only a non-empty list is carried. An empty one on every configuration means the filter
        /// was never set up rather than deliberately emptied - there was no way to express the
        /// difference in the old shape - so those servers get no row here and the startup seed
        /// plants the shipped default, which is the point of shipping one. A server with a real
        /// list keeps exactly that list and is never seeded over.
        /// </para>
        /// <para>
        /// The active configuration's list wins where there are several, since that is the one
        /// that was actually in force.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_policy",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    blocked_words = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_policy", x => x.key);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO moderation_policy (key, blocked_words)
                SELECT 'server', COALESCE(
                    (SELECT blocked_words FROM game_configurations
                     WHERE is_active = TRUE AND blocked_words <> '' LIMIT 1),
                    (SELECT blocked_words FROM game_configurations
                     WHERE blocked_words <> '' LIMIT 1))
                WHERE EXISTS (SELECT 1 FROM game_configurations WHERE blocked_words <> '');
                """);

            migrationBuilder.DropColumn(
                name: "blocked_words",
                table: "game_configurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "blocked_words",
                table: "game_configurations",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            // Back onto every configuration, because going down cannot know which one it came
            // from and a list on the wrong row is the same as no list at all.
            migrationBuilder.Sql(
                """
                UPDATE game_configurations
                SET blocked_words = COALESCE(
                    (SELECT blocked_words FROM moderation_policy WHERE key = 'server'), '');
                """);

            migrationBuilder.DropTable(
                name: "moderation_policy");
        }
    }
}
