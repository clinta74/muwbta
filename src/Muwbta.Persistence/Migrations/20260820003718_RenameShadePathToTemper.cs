using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <summary>
    /// Shade becomes Temper, everywhere the Path is written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure data, no schema.</b> <c>CharacterPath</c> persists through
    /// <c>HasConversion&lt;string&gt;()</c>, which is worth having — the column reads in a psql
    /// session and survives someone reordering the enum — and is exactly what makes a rename a
    /// migration rather than a refactor. Without this, every character on the old Path fails to
    /// materialise, and that takes their account's whole character list down with it.
    /// </para>
    /// <para>
    /// <b>Three tables carry the name, and a fourth does not.</b> <c>characters.path</c> is the
    /// enum's <em>name</em>; <c>quests.paths</c> and <c>item_templates.paths</c> are jsonb arrays
    /// of names — the epic chains and the epic weapons are Path-locked, so a rename stopping at
    /// <c>characters</c> would leave twenty quests and twenty weapons gated on a Path nobody can
    /// hold. <c>abilities.path</c> is the <em>ordinal</em>: Temper is 2 whatever it is called, and
    /// it needs nothing. Its <em>keys</em> are the exception, below. The jsonb rewrite rebuilds
    /// the array element by element rather than replacing text, so only an element that
    /// <em>is</em> the Path is touched.
    /// </para>
    /// <para>
    /// <b>Every ability key is listed, because nine of the seventeen change more than a prefix.</b>
    /// <c>AbilityValidator</c> requires a key to begin with its Path — <c>AbilityLookup</c>
    /// resolves keys as well as names, so a <c>shade.</c> key on a Temper ability is a name that
    /// resolves to nobody. A blanket <c>shade. → temper.</c> would carry the honest keys across and
    /// leave Hamstring, Ambush, Vanish, Assassinate, Death Mark, Exploit, Shadowstep, A Thousand
    /// Cuts and Severance pointing at a Path that no longer describes what they do. The prefix
    /// rewrite still runs afterwards, as a net for any <c>shade.</c> row a builder authored through
    /// the editor.
    /// </para>
    /// <para>
    /// <b>Keys and the Path, and nothing else.</b> Names and descriptions are content: they live in
    /// <c>shipped/abilities.json</c> and arrive by import. Copying seventeen names and descriptions
    /// into a migration would be a second place they are written down, disagreeing with the first
    /// the moment anybody edits the prose — the exact defect that moved abilities out of C# in the
    /// first place. A database that migrates without importing shows the old wording against the
    /// new keys, which is cosmetic and fixed by the import that follows.
    /// </para>
    /// <para>
    /// <b>The startup reconcile cannot do any of this.</b> <c>ReconcileAbilitiesAsync</c> is
    /// insert-if-absent and never updates or deletes — deliberately, so a builder's retune survives
    /// a restart. Renaming keys without a migration would leave seventeen orphan <c>shade.</c> rows
    /// and plant fresh ones beside them.
    /// </para>
    /// <para>
    /// Both directions are exact. A rollback that left half the Paths renamed would be worse than
    /// never having run.
    /// </para>
    /// </remarks>
    public partial class RenameShadePathToTemper : Migration
    {
        /// <summary>
        /// Every ability key that moves, old to new.
        /// </summary>
        /// <remarks>
        /// The eight that keep their stem are listed too, rather than left to the prefix rewrite.
        /// A reader checking whether their favourite ability survived should find it here, and a
        /// list that covers all seventeen cannot be half-right about which ones it covers.
        /// </remarks>
        private static readonly (string Shade, string Temper)[] Keys =
        [
            ("shade.strike", "temper.strike"),
            ("shade.evasion", "temper.evasion"),
            ("shade.fortify", "temper.fortify"),
            ("shade.hamstring", "temper.low-kick"),
            ("shade.ambush", "temper.body-blow"),
            ("shade.provoke", "temper.provoke"),
            ("shade.vanish", "temper.second-wind"),
            ("shade.assassinate", "temper.heart-strike"),
            ("shade.death-mark", "temper.pressure-point"),
            ("shade.rupture", "temper.rupture"),
            ("shade.exploit", "temper.opening"),
            ("shade.flurry", "temper.flurry"),
            ("shade.hemorrhage", "temper.hemorrhage"),
            ("shade.execution", "temper.execution"),
            ("shade.shadowstep", "temper.temple-strike"),
            ("shade.thousand-cuts", "temper.hundred-hands"),
            ("shade.severance", "temper.collapse"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            Rename(migrationBuilder, from: "Shade", to: "Temper", forward: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            Rename(migrationBuilder, from: "Temper", to: "Shade", forward: false);

        private static void Rename(
            MigrationBuilder migrationBuilder, string from, string to, bool forward)
        {
            var lowerFrom = from.ToLowerInvariant();
            var lowerTo = to.ToLowerInvariant();

            migrationBuilder.Sql($"UPDATE characters SET path = '{to}' WHERE path = '{from}';");

            foreach (var (shade, temper) in Keys)
            {
                var (oldKey, newKey) = forward ? (shade, temper) : (temper, shade);

                migrationBuilder.Sql(
                    $"UPDATE abilities SET key = '{newKey}' WHERE key = '{oldKey}';");
            }

            // The net, for a row somebody authored through the editor under the old name. It runs
            // after the list, so nothing above can be rewritten twice — those keys already begin
            // with the new prefix and are no longer matched.
            migrationBuilder.Sql(
                $"UPDATE abilities SET key = '{lowerTo}.' || substring(key from {lowerFrom.Length + 2}) "
                + $"WHERE key LIKE '{lowerFrom}.%';");

            foreach (var table in new[] { "quests", "item_templates" })
            {
                migrationBuilder.Sql($"""
                    UPDATE {table}
                    SET paths = (
                        SELECT jsonb_agg(CASE WHEN value = '{from}' THEN '{to}' ELSE value END)
                        FROM jsonb_array_elements_text(paths) AS value
                    )
                    WHERE paths @> '["{from}"]'::jsonb;
                    """);
            }

            // The epic line names the Path in its own keys — twenty weapons and twenty quests —
            // and those keys are referenced from four other places. None of them is a foreign key
            // (quest keys deliberately are not, so a builder can wire a quest before its target
            // exists), so every reference has to be rewritten by hand or it dangles.
            migrationBuilder.Sql(
                $"UPDATE item_templates SET key = replace(key, 'epic-{lowerFrom}-', 'epic-{lowerTo}-') "
                + $"WHERE key LIKE 'epic-{lowerFrom}-%';");

            migrationBuilder.Sql(
                $"UPDATE item_instances SET template_key = replace(template_key, 'epic-{lowerFrom}-', 'epic-{lowerTo}-') "
                + $"WHERE template_key LIKE 'epic-{lowerFrom}-%';");

            migrationBuilder.Sql($"""
                UPDATE quests SET
                    key = regexp_replace(key, '^e([1-5])-{lowerFrom}$', 'e\1-{lowerTo}'),
                    required_item_key = replace(required_item_key, 'epic-{lowerFrom}-', 'epic-{lowerTo}-'),
                    reward_item_key = replace(reward_item_key, 'epic-{lowerFrom}-', 'epic-{lowerTo}-'),
                    prerequisite_quest_keys = (
                        -- text[], not jsonb. The Paths lists next door are jsonb and these are
                        -- not, which is the kind of thing a rename finds out the hard way.
                        SELECT array_agg(regexp_replace(value, '^e([1-5])-{lowerFrom}$', 'e\1-{lowerTo}'))
                        FROM unnest(prerequisite_quest_keys) AS value
                    )
                WHERE key LIKE 'e%-{lowerFrom}'
                    OR required_item_key LIKE 'epic-{lowerFrom}-%'
                    OR reward_item_key LIKE 'epic-{lowerFrom}-%'
                    OR prerequisite_quest_keys::text LIKE '%-{lowerFrom}%';
                """);

            // A character part-way through the epic chain keeps their progress, which is the whole
            // reason this is here rather than left to the import.
            migrationBuilder.Sql($"""
                UPDATE character_quests
                SET quest_key = regexp_replace(quest_key, '^e([1-5])-{lowerFrom}$', 'e\1-{lowerTo}')
                WHERE quest_key LIKE 'e%-{lowerFrom}';
                """);
        }
    }
}
