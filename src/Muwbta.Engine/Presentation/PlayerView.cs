using Muwbta.Domain.Abilities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Systems;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Presentation;

/// <summary>
/// Turns world state into the events a client renders. Sits between the command handlers and
/// <see cref="RoomLayoutService"/> so handlers never touch coordinates themselves.
/// </summary>
public sealed class PlayerView(
    RoomLayoutService layout,
    ItemTemplateCache? items = null,
    WeatherSystem? weather = null)
{
    /// <summary>
    /// What a room says instead of its name when nobody in it has a light.
    /// </summary>
    /// <remarks>
    /// The title goes too, not only the description. Knowing you are in Khaldra's Hearth is knowing
    /// where you are, and a player who cannot see the room cannot read the sign over the door —
    /// the phone header draws from this same field, so leaving the real title in would have the
    /// game announcing your location while telling you it is pitch black.
    /// </remarks>
    public const string DarkTitle = "Darkness";

    /// <summary>
    /// Said in place of a description, and it names the answer.
    /// </summary>
    /// <remarks>
    /// "You cannot see a thing" alone reads as a broken room to somebody who has never met the
    /// flag — four whole zones are dark, and the first of them is at level 20, by which point a
    /// player has walked through two hundred rooms that all worked. Naming the light is the
    /// difference between a puzzle and a bug report.
    /// </remarks>
    public const string DarkProse = "It is pitch black. You cannot see a thing without a light.";

    private readonly RoomLayoutService _layout =
        layout ?? throw new ArgumentNullException(nameof(layout));

    /// <summary>
    /// Sends the structured panels plus the prose. Both, always: PLAN.md §5 makes the
    /// scrollback authoritative, so a player who ignores the map must miss nothing.
    /// </summary>
    public void SendRoom(WorldState world, PlayerActor actor, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(actor);

        var room = world.FindRoom(actor.RoomKey);
        if (room is null)
        {
            // The builder deleted this room out from under the player (PLAN.md §7.4).
            actor.SendText("You are nowhere at all. The world has forgotten this place.", "bad");
            return;
        }

        // Asked once for the room rather than once per thing withheld, so the four frames below
        // cannot disagree about whether the lamp is lit.
        var dark = RoomLight.IsDark(world, items, actor.RoomKey);

        IReadOnlyList<PlayerActor> occupants = dark ? [] : world.OccupantsOf(actor.RoomKey);
        IReadOnlyList<Mob> mobs = dark ? [] : world.MobsIn(actor.RoomKey);
        IReadOnlyList<ItemInstance> roomItems = dark ? [] : world.ItemsIn(actor.RoomKey);

        // Exits survive the dark, and this is the one deliberate concession in it. Walking is the
        // only way out of an unlit room, and on a phone the exit pad is drawn from this list and is
        // the only movement control there is — a dark room you can leave only by having memorised
        // the map is a trap rather than a reason to buy a lantern.
        var exits = room.Exits
            .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
            .Select(e => e.Direction.ToLowerName())
            .ToList();

        actor.Send(new OutboundEvent(
            EventTypes.Room,
            new RoomPayload(
                room.Key.ToString(),
                dark ? DarkTitle : room.Title,
                dark ? string.Empty : room.Description,
                exits)));

        var legend = LegendFor(room, dark);
        var map = _layout.BuildMap(room, occupants, mobs, roomItems);

        actor.Send(new OutboundEvent(EventTypes.Map, dark ? Unlit(map) : map));

        actor.Send(new OutboundEvent(
            EventTypes.Contents,
            BuildContents(occupants, mobs, roomItems, legend)));

        SendProse(world, actor, room, occupants, mobs, roomItems, exits, verbose, dark);
    }

    /// <summary>
    /// The room's grid with nothing drawn on it.
    /// </summary>
    /// <remarks>
    /// The same width and height rather than an empty payload, so the map panel keeps its shape and
    /// the layout does not jump every time somebody walks into an unlit room. Built from the real
    /// map so the dimensions are whatever the room's actually are.
    /// </remarks>
    private static MapPayload Unlit(MapPayload map) =>
        new(map.W, map.H, [.. map.Terrain.Select(row => new string(' ', row.Length))], []);

    /// <summary>What the blank map's characters mean. Nothing, in the dark.</summary>
    private static IReadOnlyDictionary<string, string> LegendFor(Room room, bool dark) =>
        dark
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : room.HasGrid
                ? room.Legend
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["."] = "floor" };

    /// <summary>
    /// Notes that a room's map and contents have changed, to be sent once at the end of the pulse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Marking, not sending.</b> A room is redrawn because its occupancy changed, and within one
    /// 250 ms tick that can happen many times over: twenty people walking through the same room
    /// used to mean twenty full rebuilds and twenty broadcasts of a room whose <em>final</em> state
    /// is the only one anybody needed. Nobody can perceive the nineteen intermediate frames — they
    /// are all superseded before the tick ends — so the set is deduplicated and flushed once.
    /// </para>
    /// <para>
    /// Movement marks two rooms, the one being left and the one being entered, and both collapse
    /// across everyone who moved. This is the last of the quadratic behaviour on the refresh path:
    /// laying the room out per viewer went first, then copying the payload per viewer, and what
    /// remained was the number of <em>refreshes</em> rising with the number of movers while the
    /// recipients of each rose with the occupancy (PLAN.md §11).
    /// </para>
    /// <para>
    /// <b>Thread-safe on purpose.</b> Mob AI and the spawner are launched fire-and-forget onto the
    /// thread pool, so they mark rooms from somewhere other than the loop. A mark that lands after
    /// this pulse's flush is simply sent on the next one, which is a quarter second later and
    /// already true of anything those systems do.
    /// </para>
    /// </remarks>
    public void MarkRoomChanged(RoomKey roomKey)
    {
        lock (_changedRooms)
        {
            _changedRooms.Add(roomKey);
        }
    }

    /// <summary>
    /// Sends every room marked since the last flush, once each.
    /// </summary>
    /// <remarks>
    /// Called from the game loop at the end of a pulse, after everything that could have moved
    /// somebody. The state each room is drawn from is therefore the state the tick finished in,
    /// which is the only one worth sending.
    /// </remarks>
    public void FlushChangedRooms(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        RoomKey[] rooms;

        lock (_changedRooms)
        {
            if (_changedRooms.Count == 0)
            {
                return;
            }

            rooms = [.. _changedRooms];
            _changedRooms.Clear();
        }

        foreach (var roomKey in rooms)
        {
            RefreshRoom(world, roomKey);
        }
    }

    /// <summary>Rooms marked since the last flush.</summary>
    private readonly HashSet<RoomKey> _changedRooms = [];

    /// <summary>
    /// Sends the map and contents to everyone standing in a room, now.
    /// </summary>
    /// <remarks>
    /// The flush does this; so does a test that wants one room drawn without running a pulse.
    /// <b>Ordinary game code should call <see cref="MarkRoomChanged"/> instead</b> — calling this
    /// directly from a command handler is what made a busy room cost a rebuild per mover.
    /// </remarks>
    public void RefreshRoom(WorldState world, RoomKey roomKey)
    {
        ArgumentNullException.ThrowIfNull(world);

        var room = world.FindRoom(roomKey);
        if (room is null)
        {
            return;
        }

        // The people in the room are still the ones to send to, whether or not any of them can see
        // one another - so this is read before the dark empties the lists.
        var viewers = world.OccupantsOf(roomKey);

        var dark = RoomLight.IsDark(world, items, roomKey);

        IReadOnlyList<PlayerActor> occupants = dark ? [] : viewers;
        IReadOnlyList<Mob> mobs = dark ? [] : world.MobsIn(roomKey);
        IReadOnlyList<ItemInstance> roomItems = dark ? [] : world.ItemsIn(roomKey);

        var legend = LegendFor(room, dark);

        // Two events for the whole room, built once and handed to everybody by reference.
        //
        // Refreshing a room used to be quadratic in how many people were in it, twice over: the
        // map was laid out once per viewer, and then the contents list was copied once per viewer
        // to relabel a single entry as "you". Neither had to be. Placement is a pure function of
        // the room and its occupancy, and marking the viewer is a rendering decision - so the
        // layout is hoisted and the marking has moved to the client, which is the only party that
        // knows whose screen this is. What is left is one build and N channel writes.
        //
        // Sharing the events is safe by construction: OutboundEvent and every payload beneath it
        // are immutable records, and a session does nothing to what it is handed but serialise it.
        var built = _layout.BuildMap(room, occupants, mobs, roomItems);
        var map = new OutboundEvent(EventTypes.Map, dark ? Unlit(built) : built);
        var contents = new OutboundEvent(
            EventTypes.Contents, BuildContents(occupants, mobs, roomItems, legend));

        foreach (var viewer in viewers)
        {
            viewer.Send(map);
            viewer.Send(contents);
        }
    }

    /// <summary>
    /// Sends the character's whole ability list, each with whatever is left of its cooldown.
    /// </summary>
    /// <remarks>
    /// Sent on entering the world and whenever the set could have changed - a level-up grants
    /// abilities, and a builder editing one changes what the rest of them say. Not sent per pulse:
    /// the client counts the cooldowns down itself and only needs correcting when it has been
    /// away, which is exactly when this arrives (see <see cref="AbilitiesPayload"/>).
    ///
    /// A cache-less host sends an empty list rather than throwing. That is a host that never
    /// loaded abilities, which is a test harness rather than a game.
    /// </remarks>
    public static void SendAbilities(
        PlayerActor actor,
        WorldState world,
        AbilityCache? cache,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(world);

        var character = actor.Character;

        var known = cache is null
            ? []
            : cache.All.Values
                .Where(a => a.Path == character.Path && a.UnlockLevel <= character.Level)
                .OrderBy(a => a.UnlockLevel)
                .ThenBy(a => a.Key, StringComparer.Ordinal)
                .ToList();

        var entries = known
            .Select(a => new AbilityEntry(
                a.Key,
                a.Name,
                // What a player actually types. `cast` refuses a skill (§4.7), so telling the
                // client which word to use is the difference between a panel that teaches the
                // vocabulary and one that lists keys.
                AbilityKinds.VerbFor(a),
                a.CostType.ToString(),
                a.CostValue,
                a.CooldownPulses,
                RemainingCooldown(world, character.Id, a, currentPulse),
                AbilityKinds.Of(a) == AbilityKind.Spell))
            .ToList();

        actor.LastSentAbilityLevel = character.Level;
        actor.Send(new OutboundEvent(EventTypes.Abilities, new AbilitiesPayload(entries)));
    }

    /// <summary>
    /// Resends the roster when the character has levelled since the last one.
    /// </summary>
    /// <remarks>
    /// Levelling is what changes the set, and it happens in three places. Comparing the level once
    /// per pulse costs an int and cannot be forgotten by a new one, which is the same trade
    /// <see cref="SendVitalsIfChanged"/> makes.
    ///
    /// A builder retuning an ability is deliberately *not* covered: that changes what the panel
    /// says rather than which abilities exist, and the corrected roster arrives the next time the
    /// character enters. Pushing it would mean the applier reaching every player of a Path on
    /// every save.
    /// </remarks>
    public static void SendAbilitiesIfLevelled(
        PlayerActor actor,
        WorldState world,
        AbilityCache? cache,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.LastSentAbilityLevel == actor.Character.Level)
        {
            return;
        }

        actor.LastSentAbilityLevel = actor.Character.Level;
        SendAbilities(actor, world, cache, currentPulse);
    }

    /// <summary>
    /// Pulses left of this ability's <em>own</em> cooldown. Zero when that has run out.
    /// </summary>
    /// <remarks>
    /// <b>Own cooldown, deliberately - not "when can this be used".</b> An ability held down by a
    /// timer it shares would answer differently, and the cooling bar is a list of what the player
    /// used: a group-mate they never touched appearing there would be a bar they cannot explain.
    /// It also keeps the live path and the reconnect path agreeing, since the bar counts down from
    /// this number and only ever hears about abilities that actually fired.
    ///
    /// The shared timer is answered where it is acted on, by <c>AbilityCooldowns.Blocking</c> in
    /// the cast path, and told to the player in the refusal. Both read the arithmetic below.
    /// </remarks>
    private static long RemainingCooldown(
        WorldState world,
        Guid characterId,
        Ability ability,
        long currentPulse) =>
        AbilityCooldowns.OwnRemaining(
            ability, world.GetAbilityCooldown(characterId, ability.Key), currentPulse);

    /// <summary>
    /// Names whatever the levels just crossed granted, one line each.
    /// </summary>
    /// <remarks>
    /// <b>Nothing else tells the player.</b> The roster is a panel, and the panel now shows only
    /// what is cooling - so an ability that arrives without a line here arrives without anything a
    /// player would notice. Sent to the transcript rather than pushed at the panel because that is
    /// where the game speaks.
    ///
    /// Takes the level the character started at rather than the number of levels gained: a single
    /// award can carry someone across several at once, and everything unlocked on the way is still
    /// theirs to be told about.
    ///
    /// A null actor is a character nobody is playing, and a null cache is a host that never loaded
    /// abilities - both send nothing rather than throwing, which is the same trade
    /// <see cref="SendAbilities"/> makes.
    /// </remarks>
    public static void SendUnlocks(PlayerActor? actor, AbilityCache? cache, int fromLevel)
    {
        if (actor is null || actor.Character.Level <= fromLevel)
        {
            return;
        }

        var lines = LevelUpUnlocks.Announce(
            cache?.All.Values ?? [],
            actor.Character.Path,
            fromLevel,
            actor.Character.Level);

        foreach (var line in lines)
        {
            actor.SendText(line, "levelup");
        }
    }

    /// <summary>Tells one player that an ability of theirs has started cooling down.</summary>
    public static void SendCooldown(PlayerActor actor, string abilityKey, long pulses)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.Send(new OutboundEvent(EventTypes.Cooldown, new CooldownPayload(abilityKey, pulses)));
    }

    public static void SendVitals(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var payload = VitalsOf(actor);
        actor.LastSentVitals = payload;
        actor.Send(new OutboundEvent(EventTypes.Vitals, payload));
    }

    /// <summary>
    /// Sends vitals only when they differ from the last frame this player received.
    /// </summary>
    /// <remarks>
    /// Combat resolves every pulse, so an unconditional push would be four frames a second per
    /// fighter. Comparing the payload rather than tracking a dirty flag means damage, healing,
    /// regeneration, experience and levelling are all covered by construction - there is no
    /// mutation site that can forget to mark itself.
    /// </remarks>
    public static void SendVitalsIfChanged(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var payload = VitalsOf(actor);
        if (payload == actor.LastSentVitals)
        {
            return;
        }

        actor.LastSentVitals = payload;
        actor.Send(new OutboundEvent(EventTypes.Vitals, payload));
    }

    /// <summary>Sends the group roster whether or not it has changed.</summary>
    public static void SendParty(WorldState world, PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(actor);

        var members = PartyOf(world, actor);
        actor.LastSentParty = members;
        actor.Send(new OutboundEvent(EventTypes.Party, new PartyPayload(members)));
    }

    /// <summary>
    /// Sends the group roster only when it differs from the last one this player received.
    /// </summary>
    /// <remarks>
    /// The same trade <see cref="SendVitalsIfChanged"/> makes, and for a stronger reason: this
    /// frame moves whenever any of six characters takes a hit, so the events that would have to
    /// push it are every one that touches a vital, times everyone who can see it. Comparing here
    /// means joining, leaving, being kicked, walking out of the room, going link-dead and simply
    /// getting hurt are all covered without any of them knowing the panel exists.
    ///
    /// An ungrouped player compares an empty list against an empty list and sends nothing, which is
    /// the common case and costs one allocation-free length check.
    /// </remarks>
    public static void SendPartyIfChanged(WorldState world, PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(actor);

        var members = PartyOf(world, actor);

        // Never having been sent one is the same picture as having been sent an empty one: there is
        // no group either way, and nothing on screen to correct. Treating null as "unknown" instead
        // would send every solo player a frame saying nothing, once, on their first pulse.
        if ((actor.LastSentParty ?? []).SequenceEqual(members))
        {
            return;
        }

        actor.LastSentParty = members;
        actor.Send(new OutboundEvent(EventTypes.Party, new PartyPayload(members)));
    }

    /// <summary>
    /// This player's group as they see it, or an empty list when they are adventuring alone.
    /// </summary>
    /// <remarks>
    /// A member with no actor - removed from the world between the roster being read and this
    /// running - is skipped rather than shown as a blank row. <see cref="PartyRegistry.Forget"/>
    /// takes them out on the way through the one door out of the world, so this is a window of one
    /// pulse rather than a state that persists.
    /// </remarks>
    private static IReadOnlyList<PartyMemberEntry> PartyOf(WorldState world, PlayerActor actor)
    {
        if (world.Parties.Of(actor.CharacterId) is not { } party)
        {
            return [];
        }

        var entries = new List<PartyMemberEntry>(party.Count);

        foreach (var memberId in party.Members)
        {
            if (world.FindByCharacter(memberId) is not { } member)
            {
                continue;
            }

            var c = member.Character;
            var v = c.Vitals;

            entries.Add(new PartyMemberEntry(
                member.Name,
                c.Level,
                c.Path.ToString(),
                v.Health, v.HealthMax,
                v.Focus, v.FocusMax,
                v.Stamina, v.StaminaMax,
                party.IsLeader(memberId),
                member.RoomKey == actor.RoomKey,
                member.IsLinkDead));
        }

        return entries;
    }

    private static VitalsPayload VitalsOf(PlayerActor actor)
    {
        var c = actor.Character;
        var v = c.Vitals;

        return new VitalsPayload(
            v.Health, v.HealthMax,
            v.Focus, v.FocusMax,
            v.Stamina, v.StaminaMax,
            c.Level, c.Xp, c.Path.ToString(),
            c.Gold);
    }

    private void SendProse(
        WorldState world,
        PlayerActor actor,
        Room room,
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        IReadOnlyList<string> exits,
        bool verbose,
        bool dark)
    {
        var spans = new List<TextSpan> { new(dark ? DarkTitle : room.Title, "room-title") };

        // Said whether or not the look was verbose. Brief mode suppresses a description you have
        // read before; this is not a description, it is the reason there isn't one.
        if (dark)
        {
            spans.Add(new TextSpan("\n" + DarkProse, "dark"));
        }
        else if (verbose && !string.IsNullOrWhiteSpace(room.Description))
        {
            // Classed so the client can tell prose from the structured lines around it.
            // Every other span in a look is one line by construction - Exits, an occupant,
            // a mob - and is rendered with its newlines intact. A description is the one
            // span whose newlines are the author's rather than the protocol's, so the
            // client re-flows it and a hard-wrapped paragraph fills the window instead of
            // the editor it was typed in.
            spans.Add(new TextSpan("\n" + room.Description, "room-description"));
        }

        // After the description and before the exits, because it is the last thing said about
        // the place itself and everything below is a list of what is in it or leads out of it.
        //
        // Never in the dark. A player who cannot see the room cannot see the sky over it either,
        // and a line about the weather under DarkProse would be the game describing the clouds to
        // somebody feeling for the wall.
        if (!dark && weather?.StandingLineFor(world, room.Key) is { } sky)
        {
            spans.Add(new TextSpan("\n" + sky, "weather"));
        }

        spans.Add(new TextSpan(
            exits.Count == 0
                ? "\nThere are no obvious exits."
                : $"\nExits: {string.Join(", ", exits)}",
            "exits"));

        foreach (var other in occupants.Where(o => o.CharacterId != actor.CharacterId))
        {
            var suffix = other.IsLinkDead ? " (link-dead)" : string.Empty;
            spans.Add(new TextSpan($"\n{other.Name} is here.{suffix}", "occupant"));
        }

        foreach (var mob in mobs.OrderBy(m => m.TemplateKey))
        {
            var prose = NarrationHelper.BuildSentence(MobLabel.For(mobs, mob), "is here.");
            spans.Add(new TextSpan($"\n{prose}", "mob"));
        }

        foreach (var item in items.OrderBy(i => i.TemplateKey))
        {
            var displayName = item.DisplayName;
            spans.Add(new TextSpan($"\nYou see {NarrationHelper.WithArticle(displayName)}.", "item"));
        }

        actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>
    /// What is in the room, the same for everyone in it.
    /// </summary>
    /// <remarks>
    /// No viewer, deliberately. This used to take one and copy the whole occupant list to relabel
    /// a single entry as "you" - once per person in the room, so the copying was quadratic and the
    /// comparison it did to find the entry allocated a lowercased name per entry per viewer on top.
    /// Whose screen this is belongs to the client, which relabels its own entry on arrival; the
    /// keyword is untouched either way, so the verbs a player can type still name a real character.
    /// </remarks>
    private static ContentsPayload BuildContents(
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        IReadOnlyDictionary<string, string>? legend = null)
    {
        var (occupantEntries, itemEntries) = BuildContentsFor(occupants, mobs, items);

        return new ContentsPayload(occupantEntries, itemEntries, legend);
    }

    private static (List<ContentEntry> Occupants, List<ContentEntry> Items) BuildContentsFor(
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items)
    {
        var occupantEntries = new List<ContentEntry>();
        var itemEntries = new List<ContentEntry>();

        // Add players
        occupantEntries.AddRange(occupants
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new ContentEntry(
                o.Icon,
                o.IsLinkDead ? $"{o.Name} (link-dead)" : o.Name,
                o.Name.ToLowerInvariant())));

        // Add mobs
        occupantEntries.AddRange(mobs
            .OrderBy(m => m.TemplateKey)
            .Select(m => {
                var icon = m.MapGlyph;
                return new ContentEntry(icon, MobLabel.For(mobs, m), m.TemplateKey.ToLowerInvariant());
            }));

        // Add items
        itemEntries.AddRange(items
            .OrderBy(i => i.TemplateKey)
            .Select(i => {
                var displayName = i.DisplayName;
                return new ContentEntry(i.Icon, displayName, i.TemplateKey.ToLowerInvariant());
            }));

        return (occupantEntries, itemEntries);
    }
}
