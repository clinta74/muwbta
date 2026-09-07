using Muwbta.Domain.Abilities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Persistence.Seeding;

/// <summary>
/// PLAN.md Phase 1: a hand-built starter zone, enough to have somewhere to walk before the
/// world builder exists in Phase 2. Once the builder ships this is only used to give a fresh
/// database something in it.
/// </summary>
public static class StarterWorldSeeder
{
    public const string WorldKey = "aldenmoor";
    public const string ZoneKey = "aldenmoor.millbrook";

    /// <summary>Where new characters begin, and where anyone in a deleted room is sent.</summary>
    public static RoomKey StartingRoom { get; } = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    /// <summary>
    /// Authored one way only. Reciprocal exits are generated, because hand-writing both
    /// halves is exactly how one-way passages get created by accident.
    /// </summary>
    private static readonly (string From, Direction Direction, string To)[] Links =
    [
        ("old-mill", Direction.South, "millpond"),
        ("millpond", Direction.East, "north-gate"),
        ("north-gate", Direction.North, "hill-road"),
        ("north-gate", Direction.East, "market-row"),
        ("north-gate", Direction.South, "village-green"),
        ("market-row", Direction.South, "tavern-door"),
        ("village-green", Direction.West, "smithy"),
        ("village-green", Direction.East, "tavern-door"),
        ("village-green", Direction.South, "well-yard"),
        ("tavern-door", Direction.South, "tavern-common"),
        ("well-yard", Direction.West, "chapel-steps"),
        ("chapel-steps", Direction.Down, "chapel-nave"),
    ];

    /// <summary>
    /// The starter zone's rooms.
    /// </summary>
    /// <remarks>
    /// Grids are 21x9 - roughly double the old 11x5 - and drawn with box-drawing borders instead
    /// of hashes. The extra room is what makes furniture legible: a tavern with four tables and a
    /// bar reads as a tavern, where at 11x5 it could only afford a suggestion of one. Six of these
    /// rooms had no art at all and rendered as a blank rectangle.
    ///
    /// Every glyph must be a single BMP character. <c>RoomLayoutService</c> indexes terrain rows
    /// per <c>char</c>, so anything outside the basic plane would be split across two cells and
    /// draw as mojibake - which rules out emoji. Box-drawing, block, and geometric shapes are all
    /// safe, and are what a monospace font renders at exactly one cell wide.
    /// </remarks>
    private static readonly RoomSeed[] Rooms =
    [
        new("old-mill", "The Old Mill", 0, 0,
            "The mill wheel has not turned in years. Its paddles hang black with rot, and "
            + "the axle beam sags where the water no longer reaches it. Somewhere inside, "
            + "something small scurries along a rafter.",
            [
                "┌───────────────────┐",
                "│░░...............░░│",
                "│..┌───────────┐....│",
                "│..│.....◎.....│....│",
                "│..└───────────┘....│",
                "│░.................░│",
                "├───────────────────┤",
                "│≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈│",
                "└───────────────────┘",
            ],
            new() { ["."] = "floor", ["≈"] = "water", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["├"] = "wall", ["┤"] = "wall", ["░"] = "rubble", ["◎"] = "well" }),

        new("millpond", "The Millpond", 0, 1,
            "Still green water, thick with duckweed, holds the sky upside down. Reeds crowd "
            + "the near bank. A heron stands motionless in the shallows and does not "
            + "acknowledge you.",
            [
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"≈≈≈≈≈≈≈≈≈≈≈\"\"\"\"\"\"",
                "\"\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"",
                "\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"\"≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈\"\"",
                "\"\"\"\"≈≈≈≈≈≈≈≈≈≈≈\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["≈"] = "water" }),

        new("hill-road", "The Hill Road", 2, 0,
            "Cart ruts climb north into brown hills and lose themselves in the gorse. The "
            + "wind up here carries no smell of the village at all.",
            [
                "\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"·\"·\"\"\"\"\"\"\"\"\"\"",
                "\"♣\"\"\"\"\"\"·\"\"·\"\"\"♣\"\"\"\"\"",
                "\"\"\"\"\"\"\"·\"\"\"\"·\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"·\"\"\"\"\"\"·\"\"\"♣\"\"\"",
                "\"\"\"\"\"·\"\"\"\"\"\"\"\"·\"\"\"\"\"\"",
                "\"\"\"\"·\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["♣"] = "tree" }),

        new("north-gate", "The North Gate", 2, 1,
            "A weathered portcullis stands half-raised above the road, its iron teeth furred "
            + "with rust. Nobody has lowered it in living memory, and the winch house has "
            + "been given over to swallows.",
            [
                "┌────────╥─╥────────┐",
                "│........│.│........│",
                "│........│.│........│",
                "│░.......│.│.......░│",
                "│........···........│",
                "│...................│",
                "│░.................░│",
                "│........···........│",
                "└────────╨─╨────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["╨"] = "gate", ["░"] = "rubble" }),

        new("market-row", "Market Row", 4, 1,
            "Empty trestle tables lean against the shopfronts, stacked for a market day that "
            + "is not today. Straw and broken crate slats drift against the kerb.",
            [
                "┌───────────────────┐",
                "│▬▬...▬▬...▬▬...▬▬..│",
                "│...................│",
                "│░.................░│",
                "│...................│",
                "│..▬▬...▬▬...▬▬...▬▬│",
                "│...................│",
                "│░░...............░░│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["░"] = "rubble", ["▬"] = "table" }),

        new("village-green", "The Village Green", 2, 2,
            "Cropped grass, a lightning-split oak, and a stone bench worn smooth by "
            + "generations of sitting. Paths run off in every direction, all of them shorter "
            + "than they look.",
            [
                "\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"··\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "·········\"♠\"·········",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"═══\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["═"] = "bench", ["♠"] = "oak", ["♣"] = "tree" }),

        new("smithy", "The Smithy", 0, 2,
            "Heat rolls out of the open front like a held breath. Horseshoes hang in graded "
            + "rows along the wall, and the anvil bears a bright crescent where the hammer "
            + "always lands.",
            [
                "┌───────────────────┐",
                "│▲▲.......░.........│",
                "│▲▲.................│",
                "│.........▄.........│",
                "│...................│",
                "│░...............═══│",
                "│...................│",
                "│...................│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench", ["▄"] = "anvil", ["░"] = "rubble", ["▲"] = "forge" },
            // Open at the front and roofed over the rest, which is what an "open front" is. The
            // rain does not reach the anvil, so neither does the weather line.
            [RoomFlags.Indoors.Key]),

        new("tavern-door", "Outside the Drowned Rat", 4, 2,
            "A painted sign shows a rodent floating cheerfully in a tankard. The door beneath "
            + "it stands open, and warm noise spills out into the street.",
            [
                "┌────────┐░░░┌──────┐",
                "│........│...│......│",
                "│........└───┘......│",
                "│..................·│",
                "└───────╥╥─────────·│",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"·\"\"\"",
                "················\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["░"] = "rubble", ["♣"] = "tree" }),

        new("tavern-common", "The Drowned Rat", 4, 3,
            "Low beams, lower conversation, and a fire that has been burning so long the "
            + "stones behind it have gone the colour of old tea. The floor is sticky in a way "
            + "nobody wants explained.",
            [
                "┌───────────────────┐",
                "│..▬▬.....▬▬........│",
                "│..▬▬.....▬▬........│",
                "│................▲▲.│",
                "│...................│",
                "│..▬▬.....▬▬........│",
                "│..▬▬.....▬▬........│",
                "│═══════════════....│",
                "└─────────··────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench", ["▬"] = "table", ["▲"] = "forge" },
            // Indoors and not peaceful. A tavern is the obvious safe room and this one cannot be:
            // the rats spawn here, and they are the first fight a new character ever picks.
            [RoomFlags.Indoors.Key]),

        new("well-yard", "The Well Yard", 2, 3,
            "A round stone well with a rope that disappears into dark. Somebody has left a "
            + "chipped cup on the rim for whoever comes thirsty next.",
            [
                "\"\"\"\"\"\"\"\"\"··\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"┌───┐\"\"·\"\"\"\"\"\"\"\"\"\"",
                "···│.◎.│·············",
                "\"\"\"└───┘\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"·\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"·\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["."] = "floor", ["·"] = "path", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["◎"] = "well", ["♣"] = "tree" }),

        new("chapel-steps", "The Chapel Steps", 0, 3,
            "Shallow steps, hollowed in the middle by centuries of feet, run down to a door "
            + "of grey wood. Moss has taken the north side of every one of them.",
            [
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"♣\"\"\"",
                "\"\"\"\"\"┌─────────┐\"\"\"\"\"",
                "·····│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"│≡≡≡≡≡≡≡≡≡│\"\"\"\"\"",
                "\"\"\"\"\"└────╥╥───┘\"\"\"\"\"",
                "\"\"♣\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
                "\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"\"",
            ],
            new() { ["\""] = "grass", ["·"] = "path", ["≡"] = "stairs", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["╥"] = "gate", ["♣"] = "tree" }),

        new("chapel-nave", "The Chapel Nave", 0, 4,
            "Cold, quiet, and smelling of stone dust. Six rows of benches face an altar with "
            + "nothing on it. The light from the high windows arrives already tired.",
            [
                "┌─────────··────────┐",
                "│.══════..═════════.│",
                "│.══════..═════════.│",
                "│.══════..═════════.│",
                "│........†..........│",
                "│.══════..═════════.│",
                "│.══════..═════════.│",
                "│.══════..═════════.│",
                "└───────────────────┘",
            ],
            new() { ["."] = "floor", ["·"] = "path", ["†"] = "altar", ["─"] = "wall", ["│"] = "wall", ["┌"] = "wall", ["┐"] = "wall", ["└"] = "wall", ["┘"] = "wall", ["═"] = "bench" },
            // The starter world's one bed. `sleep` needs `peaceful` and nothing here declared it,
            // so a new character had the best recovery in the game and nowhere at all to use it.
            // `noMob` comes with it: a wandering rat in a peaceful room is a rat that cannot be
            // removed from it.
            [RoomFlags.Indoors.Key, RoomFlags.Peaceful.Key, RoomFlags.NoMob.Key])
    ];

    public static async Task<bool> SeedAsync(
        MuwbtaDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Worlds.AnyAsync(cancellationToken))
        {
            return false;
        }

        db.Worlds.Add(new World
        {
            Key = WorldKey,
            Name = "Aldenmoor",
            Description = "A damp northern kingdom of failing villages and older stone.",
            SortOrder = 0,
        });

        db.Zones.Add(new Zone
        {
            Key = ZoneKey,
            WorldKey = WorldKey,
            Name = "Millbrook",
            Description = "A village that has been slowly emptying for two generations.",
            MinLevel = 1,
            MaxLevel = 5,
        });

        foreach (var seed in Rooms)
        {
            db.Rooms.Add(new Room
            {
                Key = RoomKey.Create(WorldKey, "millbrook", seed.Slug),
                ZoneKey = ZoneKey,
                Title = seed.Title,
                Description = seed.Description,
                Grid = [.. seed.Grid],
                Legend = new Dictionary<string, string>(seed.Legend, StringComparer.Ordinal),
                EditorX = seed.EditorX,
                EditorY = seed.EditorY,
                Flags = FlagsFor(seed),
            });
        }

        foreach (var (from, direction, to) in Links)
        {
            var fromKey = RoomKey.Create(WorldKey, "millbrook", from);
            var toKey = RoomKey.Create(WorldKey, "millbrook", to);

            db.RoomExits.Add(new RoomExit
            {
                FromRoomKey = fromKey,
                Direction = direction,
                ToRoomKey = toKey,
            });

            db.RoomExits.Add(new RoomExit
            {
                FromRoomKey = toKey,
                Direction = direction.Opposite(),
                ToRoomKey = fromKey,
            });
        }

        // The inhabitants, added only with the world: a database that already has one is a
        // database somebody has been building in, and a template row arriving under their feet
        // is the surprise the import path exists to prevent. tools/export-seed.cs is the way to
        // bring these into a database that already exists.
        db.MobTemplates.AddRange(MobTemplates);
        db.ItemTemplates.AddRange(ItemTemplates);
        db.Quests.AddRange(Quests);
        db.Spawners.AddRange(Spawners);

        // Abilities are reconciled separately, on every startup - see ReconcileAbilitiesAsync.
        // So is the starter configuration - see ReconcileStarterConfigurationAsync.
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// What the builder assist is told about Aldenmoor (PLAN.md §4.16). Short on purpose: a
    /// sandbox needs a register and a map, not a theology.
    /// </summary>
    /// <remarks>
    /// Without this, a development server's assist read the Reaches - the only canon there was -
    /// and drafted Millbrook rooms with rims and gates in them. The configuration carries its own
    /// now, and the embedded WORLD.md is what a configuration with <em>no</em> canon gets, which
    /// is what production wants and what development did not.
    /// </remarks>
    public const string StarterCanon = """
        # Aldenmoor

        The development world: the village of Millbrook and whatever a builder has added around it,
        kept as the fixture the playtest plans run against and as a sandbox for trying the builder.
        Nothing here is linked to the Reaches and nothing here is canon for them.

        ## Setting

        Aldenmoor is a damp northern kingdom of failing villages and older stone. Millbrook is one
        of the villages, two generations into emptying: the mill has stopped, the market is not
        held, and the portcullis on the north gate has rusted half-raised because nobody has
        needed to lower it in living memory. The people who remain are practical and unhurried.
        The Drowned Rat is warm and the chapel is cold, and both are open.

        ## Tone

        Plain, close, faintly damp. Describe what is there: rot on the mill paddles, a heron that
        does not acknowledge you, straw drifting against the kerb. No dread and no doom. Things
        are old and running down and nobody is dramatic about it. Present tense, one short paragraph
        of two or three sentences - twenty-five to thirty-five words is the whole of it - and the
        room describes what is there rather than how to feel about it. Second person only where it
        falls out naturally, the way the heron does; that is one room in the village, not the
        habit.

        ## Places

        The Old Mill and the Millpond to the west. The Hill Road climbing north into gorse. The
        North Gate, where new characters wake. Market Row. The Village Green at the centre, with
        its lightning-split oak and a bench worn smooth. The Smithy. The Drowned Rat and the street
        outside it. The Well Yard. The Chapel Steps, and the Chapel Nave below them. The zone is
        `aldenmoor.millbrook`, levels 1 to 5; a new zone stays under `aldenmoor.` and keeps to the
        same register.

        ## What lives here

        Rats in the tavern, Nell behind the bar of the Drowned Rat, Old Marrow at his end table
        with an empty glass, and whatever a builder adds to test with: a priest with an empty
        altar, a road bandit or two on the Hill Road. Keep it small and local. Names are plain
        English with an article, "a rat", "a hill bandit"; named people are capitalised with a
        clause, "Nell, who keeps the Drowned Rat". Keys are `aldenmoor-<thing>`, or bare for
        staples like `bread`. Loot is honest and a little worn.

        ## Keys

        Room keys are `aldenmoor.millbrook.<slug>`: three segments, lowercase letters, digits and
        hyphens. Exits pair up. Nothing here has a god, a gate, or a rim.
        """;

    /// <summary>The starter configuration's key (PLAN.md §4.16).</summary>
    public const string ConfigurationKey = "aldenmoor-starter";

    /// <summary>
    /// Plants a <see cref="GameConfiguration"/> matching the engine's compiled fallback, so the
    /// value the server is obeying is visible and editable rather than implicit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="SeedAsync"/>, because that one skips a database that already has
    /// a world.</b> Every development database created before configurations existed has a world
    /// and no configuration, and would never have got one - the Setup tab would show an empty list
    /// beside a server quietly starting people in Millbrook, which is precisely the implicit state
    /// §4.16 exists to end.
    /// </para>
    /// <para>
    /// <b>It activates only when nothing else is.</b> Creating the row is safe to repeat; stealing
    /// the active flag would not be, because a restart must never undo an operator's choice of
    /// which world their server opens in. Zero-active is reachable only on a fresh database - the
    /// API refuses to delete the live one - so this claims the slot exactly once.
    /// </para>
    /// <para>
    /// Development-only, like the world it describes. A production server has no Aldenmoor, so a
    /// configuration pointing into it would be a row naming a room that does not exist.
    /// </para>
    /// </remarks>
    /// <returns>True when a configuration was planted.</returns>
    public static async Task<bool> ReconcileStarterConfigurationAsync(
        MuwbtaDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.GameConfigurations
            .FirstOrDefaultAsync(c => c.Key == ConfigurationKey, cancellationToken);

        if (existing is not null)
        {
            // A row planted before the canon existed gets it once. A builder's own text is never
            // replaced: empty is the only state this fills, and empty is what those rows have.
            if (string.IsNullOrWhiteSpace(existing.Canon))
            {
                existing.Canon = StarterCanon;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return false;
        }

        var anyActive = await db.GameConfigurations.AnyAsync(c => c.IsActive, cancellationToken);

        db.GameConfigurations.Add(new GameConfiguration
        {
            Key = ConfigurationKey,
            Name = "Aldenmoor",
            Description =
                "The original starter world, kept as a sandbox and as the fixture the playtest "
                + "plans run against. Matches the engine's built-in fallback.",
            StartingRoomKey = StartingRoom.ToString(),

            // The line GameLoop used to hold as a literal, now where it can be changed.
            WelcomeMessage = "Welcome to Aldenmoor, {name}.",
            Canon = StarterCanon,
            IsActive = !anyActive,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Who and what lives in Millbrook: two rats, the landlady, the old man, what she sells,
    /// and one errand between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Authored in the builder on a development server, then brought here</b>, so a fresh
    /// database has something to fight, buy and talk to on the first evening rather than twelve
    /// empty rooms. It is the smallest set that exercises every loop the engine has - spawn,
    /// wander, attack, loot, shop, talk, quest - and it is deliberately not more than that: this
    /// is a fixture the playtest plans and the tests run against, and every row added here is a
    /// row those have to keep agreeing with.
    /// </para>
    /// <para>
    /// The names follow the content conventions the Reaches settled (docs/STORY.md §3.2): an
    /// article on a creature, a name and a clause on a person, keys under <c>aldenmoor-</c> with
    /// the two staples bare because the Reaches use those keys too. Spawner ids are fixed so an
    /// export of this world re-imports without doubling the tavern.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MobTemplate> MobTemplates { get; } =
    [
        new()
        {
            Key = "aldenmoor-rat",
            Name = "a rat",
            Description = "A common brown rat, scurrying amongst the debris. Beady, bright-eyed, "
                + "whiskers twitching. It seems more frightened than aggressive.",
            Icon = "r",
            Level = 1,
            BaseStats = new Dictionary<string, object> { ["health"] = 8, ["damage"] = "1-3" },
            BaseXp = 10,
            BaseGold = 0,
            Behavior = new Dictionary<string, object>
            {
                ["type"] = "passive",
                ["wanders"] = true,
                ["emotes"] = new List<object>
                {
                    new Dictionary<string, object> { ["text"] = "scurries along the floor", ["minSeconds"] = 90, ["maxSeconds"] = 120 },
                },
            },
            Loot = [new Dictionary<string, object> { ["itemTemplateKey"] = "aldenmoor-rat-skin", ["chance"] = 0.25 }],
            Attacks = [new MobAttack { Verb = "bites", DelayPulses = 8 }],
        },
        new()
        {
            Key = "aldenmoor-nell",
            Name = "Nell, who keeps the Drowned Rat",
            Description = "She keeps the fire, the bar and the peace, in that order, and has the arms "
                + "for all three. The floor is sticky and she knows.",
            Icon = "@",
            Level = 5,
            BaseStats = new Dictionary<string, object> { ["health"] = 40 },
            BaseXp = 0,
            BaseGold = 0,
            Behavior = new Dictionary<string, object>
            {
                ["type"] = "npc",
                ["wanders"] = false,
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "bread", "waterskin", "aldenmoor-ale" },
                ["markup"] = 0.1,
                ["greeting"] = new List<object>
                {
                    "'Sit anywhere that is not on fire. Ale is two, bread is one, and the old man is not for sale.'",
                },
                ["emotes"] = new List<object>
                {
                    new Dictionary<string, object> { ["text"] = "wipes the same patch of bar for the third time", ["minSeconds"] = 200, ["maxSeconds"] = 400 },
                },
            },
        },
        new()
        {
            Key = "aldenmoor-old-marrow",
            Name = "Old Marrow, who has forgotten your name",
            Description = "He has sat at the end table since before the mill stopped, and he can tell "
                + "you what the village was like then, if you do not mind hearing it twice.",
            Icon = "@",
            Level = 5,
            BaseStats = new Dictionary<string, object> { ["health"] = 40 },
            BaseXp = 0,
            BaseGold = 0,
            Behavior = new Dictionary<string, object>
            {
                ["type"] = "npc",
                ["wanders"] = false,
                ["greeting"] = new List<object>
                {
                    "'When I was younger... Who are you again?'",
                    "'Do you think it will rain?'",
                    "'What time is it?'",
                },
                ["emotes"] = new List<object>
                {
                    new Dictionary<string, object> { ["text"] = "turns the empty glass in front of him", ["minSeconds"] = 120, ["maxSeconds"] = 140 },
                },
            },
        },
    ];

    /// <inheritdoc cref="MobTemplates"/>
    public static IReadOnlyList<ItemTemplate> ItemTemplates { get; } =
    [
        new()
        {
            Key = "aldenmoor-rat-skin",
            Name = "a rat skin",
            Description = "Small, grey, and worth about what you would think. The tanner will not thank you.",
            Icon = "i",
            Weight = 1,
            BaseValue = 2,
        },
        new()
        {
            Key = "bread",
            Name = "a loaf of bread",
            Description = "Yesterday's, at best. It does the job.",
            Icon = "i",
            Weight = 1,
            BaseValue = 1,
            FoodValue = 5,
        },
        new()
        {
            Key = "waterskin",
            Name = "a waterskin",
            Description = "A skin of some animal, filled from the well.",
            Icon = "i",
            Weight = 2,
            BaseValue = 1,
            DrinkValue = 3,
        },
        new()
        {
            Key = "aldenmoor-ale",
            Name = "a pint of the Drowned Rat's ale",
            Description = "Cool, brown, and with a head on it that Nell is proud of.",
            Icon = "i",
            Weight = 1,
            BaseValue = 2,
            DrinkValue = 1,
        },
    ];

    /// <inheritdoc cref="MobTemplates"/>
    public static IReadOnlyList<Quest> Quests { get; } =
    [
        new()
        {
            Key = "millbrook-a-drink-for-the-old-man",
            ZoneKey = ZoneKey,
            Name = "A Drink for the Old Man",
            Summary = "Buy Old Marrow a pint and take it to him.",
            Description = "Nell would like the old man kept happy. He has done a lot for the Drowned "
                + "Rat, and a pint from the bar is the going rate.",
            GiverMobKey = "aldenmoor-nell",
            TurninMobKey = "aldenmoor-old-marrow",
            RequiredItemKey = "aldenmoor-ale",
            RequiredCount = 1,
            RewardXp = 25,
            RewardGold = 0,
            IsRepeatable = true,
            Dialogue = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "'Could you keep the customers happy for me? The old man has done a lot for "
                    + "this place. Buy him a <drink> and take it over, and I will call it a favour returned.'",
                ["giverInProgress"] = "'He looks thirsty. A pint from the bar, and take it to him yourself. He likes to be handed things.'",
                ["giverComplete"] = "'He liked that. He will not remember it, but he liked it.'",
                ["turninReady"] = "Old Marrow takes the pint in both hands and nods, slowly, as though you had said something wise.",
            },
        },
    ];

    /// <inheritdoc cref="MobTemplates"/>
    public static IReadOnlyList<Spawner> Spawners { get; } =
    [
        new()
        {
            Id = Guid.Parse("9d3c7d2e-0000-4000-8000-0000000000a1"),
            ZoneKey = ZoneKey,
            TemplateKey = "aldenmoor-rat",
            TemplateKind = TemplateKind.Mob,
            RoomKeys = ["aldenmoor.millbrook.tavern-common"],
            TargetCount = 2,
            RespawnSeconds = 120,
            Wanders = true,
        },
        new()
        {
            Id = Guid.Parse("9d3c7d2e-0000-4000-8000-0000000000a2"),
            ZoneKey = ZoneKey,
            TemplateKey = "aldenmoor-nell",
            TemplateKind = TemplateKind.Mob,
            RoomKeys = ["aldenmoor.millbrook.tavern-common"],
            TargetCount = 1,
            RespawnSeconds = 60,
        },
        new()
        {
            Id = Guid.Parse("9d3c7d2e-0000-4000-8000-0000000000a3"),
            ZoneKey = ZoneKey,
            TemplateKey = "aldenmoor-old-marrow",
            TemplateKind = TemplateKind.Mob,
            RoomKeys = ["aldenmoor.millbrook.tavern-common"],
            TargetCount = 1,
            RespawnSeconds = 60,
        },
    ];

    /// <summary>The room's declared flags, as a set. Empty for a room that declares none.</summary>
    private static FlagSet FlagsFor(RoomSeed seed)
    {
        var flags = new FlagSet();

        foreach (var key in seed.Flags ?? [])
        {
            flags.Set(key, true);
        }

        return flags;
    }

    /// <param name="Flags">
    /// Room flags to declare true, from <see cref="RoomFlags"/>. Absent is not the same as false:
    /// a key left out falls through to the zone and the world, which is what makes a village of
    /// mostly-outdoor rooms cost three flags rather than twelve (PLAN.md §4.10).
    /// </param>
    private sealed record RoomSeed(
        string Slug,
        string Title,
        int EditorX,
        int EditorY,
        string Description,
        string[] Grid,
        Dictionary<string, string> Legend,
        string[]? Flags = null);

    /// <summary>What a reconcile did, for the startup log.</summary>
    public readonly record struct AbilityReconciliation(int Added, int Updated, int Removed)
    {
        public bool ChangedAnything => Added > 0 || Updated > 0 || Removed > 0;
    }

    /// <summary>
    /// Plants any ability from <see cref="AbilityCatalogue"/> that the database does not have.
    /// Never updates and never deletes.
    /// </summary>
    /// <remarks>
    /// <b>This used to make the table match the catalogue exactly, and that is precisely what it
    /// must no longer do.</b> The old version's own note said why: *"If abilities ever become
    /// builder-editable, this has to become a merge instead — at that point a row differing from
    /// the catalogue stops meaning stale."* Abilities are builder-editable now, so a row that
    /// differs is somebody's retune, and updating or deleting it would throw their work away on
    /// the next restart — silently, and on every restart after that.
    ///
    /// What survives from the old behaviour is the half that fixed a real bug: **a missing row is
    /// still planted**. Seeding once meant a database that already existed never received an
    /// ability added later, so a character levelling into it was granted a key with nothing behind
    /// it and could not cast what the game said they knew. Insert-if-absent keeps that fixed
    /// without claiming authority over anything already there.
    ///
    /// The two directions deliberately given up, and what replaces them:
    ///
    /// - **Retuning** no longer arrives this way. Editing the catalogue changes what a *fresh*
    ///   database is born with and nothing else. An existing deployment gets a retune the way it
    ///   gets any other content change — an import, or a migration when the change has to be
    ///   automatic (PLAN.md §6.1).
    /// - **Orphan cleanup** is now a deliberate delete through the builder. A key the catalogue
    ///   drops stays in the table, because this code can no longer tell "the catalogue moved on"
    ///   from "somebody authored an ability we have never heard of" — and guessing wrong deletes
    ///   content.
    /// </remarks>
    public static async Task<AbilityReconciliation> ReconcileAbilitiesAsync(
        MuwbtaDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Abilities
            .ToDictionaryAsync(a => a.Key, StringComparer.Ordinal, cancellationToken);

        var catalogue = AbilityCatalogue.All.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var toAdd = catalogue.Values
            .Where(entry => !existing.ContainsKey(entry.Key))
            .ToList();

        if (toAdd.Count == 0)
        {
            return new AbilityReconciliation(0, 0, 0);
        }

        db.Abilities.AddRange(toAdd.Select(AbilityCatalogue.ToAbility));
        await db.SaveChangesAsync(cancellationToken);

        // Updated and Removed stay on the result and stay zero. The shape is kept because the
        // startup log line and its test read it, and because a future merge that *can* tell a
        // stale row from an authored one would fill them in - reporting "0 updated" is a truthful
        // statement about what this pass does, where dropping the fields would erase the question.
        return new AbilityReconciliation(toAdd.Count, 0, 0);
    }
}
