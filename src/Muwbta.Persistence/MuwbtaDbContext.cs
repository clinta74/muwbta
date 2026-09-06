using Muwbta.Domain.Abilities;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Building;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Moderation;
using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Persistence;

/// <summary>
/// PLAN.md §6: Postgres is the only source of truth. There are no content files.
/// </summary>
public sealed class MuwbtaDbContext(DbContextOptions<MuwbtaDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<Ability> Abilities => Set<Ability>();

    /// <summary>
    /// Named starter configurations, at most one of them active. See <see cref="GameConfiguration"/>
    /// for why the starting room and the greeting are content rather than deployment settings, and
    /// why a server holds several and swaps between them.
    /// </summary>
    public DbSet<GameConfiguration> GameConfigurations => Set<GameConfiguration>();

    public DbSet<ModerationPolicy> ModerationPolicies => Set<ModerationPolicy>();

    public DbSet<World> Worlds => Set<World>();

    public DbSet<WorldMap> WorldMaps => Set<WorldMap>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<RoomExit> RoomExits => Set<RoomExit>();

    public DbSet<ItemTemplate> ItemTemplates => Set<ItemTemplate>();

    public DbSet<ItemInstance> ItemInstances => Set<ItemInstance>();

    public DbSet<MobTemplate> MobTemplates => Set<MobTemplate>();

    // There is deliberately no DbSet<Mob>. A mob is a population, not a record: spawners
    // rebuild the world's inhabitants on every sweep (§4.8), so a persisted mob would be a
    // second, staler answer to a question the spawner already answers. See §6.
    public DbSet<Spawner> Spawners => Set<Spawner>();

    public DbSet<Quest> Quests => Set<Quest>();

    public DbSet<CharacterQuest> CharacterQuests => Set<CharacterQuest>();

    public DbSet<ContentAudit> ContentAudits => Set<ContentAudit>();

    public DbSet<AdminAudit> AdminAudits => Set<AdminAudit>();

    /// <summary>Non-interactive credentials for the builder API (docs/PAT-AND-MCP.md).</summary>
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Case-insensitive text for names and emails, so Kael and kael cannot both register.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MuwbtaDbContext).Assembly);

        // After the configurations, so an explicit HasColumnName still wins.
        SnakeCaseNaming.ApplyTo(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}
