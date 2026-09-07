using System.Globalization;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Entities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Spawning;

namespace Muwbta.Engine.Commands;

/// <summary>
/// Builder verbs typed in the game window (PLAN.md §7.6).
/// </summary>
/// <remarks>
/// These exist for the things that are one short argument. Nobody should type a four-sentence
/// room description onto a command line, so the builder panel owns prose and this owns
/// geography: walking somewhere and saying "there is a room that way" is far faster than
/// clicking through a tree, and it makes the exit graph correct by construction rather than
/// wired up afterwards in a form.
/// </remarks>
internal static class BuilderCommands
{
    /// <summary>
    /// What `spawn` needs, captured per registration rather than held in statics.
    /// </summary>
    /// <remarks>
    /// These four were <c>private static</c> and assigned in <see cref="Register"/>. Every sibling
    /// command file documents that exact pattern as a fixed bug — a static made the
    /// last-constructed registry the one every other registry read from, which is harmless in the
    /// server, where one is built, and a race in the tests, which build one per case and run
    /// classes in parallel. Builder commands were simply missed (BUGS.md #25).
    ///
    /// Captured in a closure, the way <c>AbilityCommands</c> passes its cache and clock.
    /// </remarks>
    private sealed record SpawnTools(
        IMobTemplateRepository? MobTemplates,
        IItemTemplateRepository? ItemTemplates,
        MobSpawner? MobSpawner,
        ItemSpawner? ItemSpawner);

    public static void Register(List<CommandDefinition> commands,
        IMobTemplateRepository? mobTemplates = null,
        IItemTemplateRepository? itemTemplates = null,
        MobSpawner? mobSpawner = null,
        ItemSpawner? itemSpawner = null)
    {
        var tools = new SpawnTools(mobTemplates, itemTemplates, mobSpawner, itemSpawner);
        // Full words, no prefixes. These are rare, destructive, and share their first letters
        // with movement - "d" must stay "down" forever.
        commands.Add(new CommandDefinition(
            "dig", 3, "dig <dir> [into <zone>] - create and link a room (builder)", Dig, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "link", 4, "link <dir> <room-key> - point an exit at an existing room (builder)", Link, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "unlink", 6, "unlink <dir> - remove an exit (builder)", Unlink, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "rtitle", 6, "rtitle <text> - retitle the room you are in (builder)", RTitle, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "rflag", 5, "rflag [<flag> [on|off]] - list or set room flags (builder)", RFlag, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "goto", 4, "goto <room-key> - jump anywhere, no exits required (builder)", Goto, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "spawn", 5,
            "spawn <item|mob> <template-key> / spawn gold <amount> - create something here (builder)",
            ctx => Spawn(ctx, tools), Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "despawn", 5, "despawn <item|mob> <template-key> - remove an item or mob here (builder)", Despawn, Requires: AccountRole.Builder));
    }


    private static void Dig(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        // "dig north into aldenmoor.crypt" - the zone suffix is how deliberate border work
        // happens. Without it a dig never silently crosses a zone boundary.
        var argument = ctx.Argument;
        string? zoneKey = null;

        var into = argument.IndexOf(" into ", StringComparison.OrdinalIgnoreCase);
        if (into >= 0)
        {
            zoneKey = argument[(into + 6)..].Trim();
            argument = argument[..into].Trim();
        }

        if (!DirectionExtensions.TryParse(argument, out var direction))
        {
            ctx.Reply("Dig which way? north, east, south, west, up, or down.", "bad");
            return;
        }

        var result = ctx.Edit(new DigRoom(ctx.Actor.RoomKey, direction, Reciprocal: true, zoneKey));

        if (!result.Success)
        {
            ctx.Reply(result.Message ?? "You cannot dig there.", "bad");
            return;
        }

        ctx.Reply(
            $"You open a way {direction.ToLowerName()}. New room: {result.AffectedRoom}.",
            "heading");
        ctx.BroadcastSight($"{ctx.Actor.Name} opens a way {direction.ToLowerName()}.", "movement");
    }

    private static void Link(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !DirectionExtensions.TryParse(parts[0], out var direction))
        {
            ctx.Reply("Usage: link <direction> <world.zone.room>", "bad");
            return;
        }

        if (!RoomKey.TryParse(parts[1].Trim(), out var target))
        {
            ctx.Reply($"'{parts[1].Trim()}' is not a room key.", "bad");
            return;
        }

        var result = ctx.Edit(new LinkExit(ctx.Actor.RoomKey, direction, target));

        ctx.Reply(
            result.Success
                ? $"{direction.ToLowerName()} now leads to {target}."
                : result.Message ?? "That link could not be made.",
            result.Success ? "heading" : "bad");
    }

    private static void Unlink(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!DirectionExtensions.TryParse(ctx.Argument, out var direction))
        {
            ctx.Reply("Usage: unlink <direction>", "bad");
            return;
        }

        var result = ctx.Edit(new UnlinkExit(ctx.Actor.RoomKey, direction));

        ctx.Reply(
            result.Success
                ? $"The way {direction.ToLowerName()} closes."
                : result.Message ?? "That exit could not be removed.",
            result.Success ? "heading" : "bad");
    }

    private static void RTitle(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: rtitle <new title>", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var upsert = WorldMutationApplier.ToUpsert(room) with { Title = ctx.Argument.Trim() };
        var result = ctx.Edit(upsert);

        ctx.Reply(
            result.Success ? "Retitled." : result.Message ?? "That title could not be set.",
            result.Success ? "heading" : "bad");
    }

    private static void RFlag(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        // Bare "rflag" lists everything with where each value came from. That answers the
        // question a builder actually has - "why is this room PvP?" - which a plain on/off
        // list would not (PLAN.md §4.10).
        if (!ctx.HasArgument)
        {
            ListFlags(ctx, room);
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];

        var flag = RoomFlags.All.FirstOrDefault(
            f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase));

        if (flag is null)
        {
            ctx.Reply(
                $"'{name}' is not a room flag. Known: {string.Join(", ", RoomFlags.All.Select(f => f.Key))}",
                "bad");
            return;
        }

        // Three states, not two: "clear" removes the key so the room inherits again, which is
        // different from setting it explicitly false.
        //
        // A word that is none of the nine is refused rather than read as "on". The fallthrough
        // used to be `_ => true`, so `rflag pvp of` — one keystroke short of "off" — turned PvP on
        // and reported that it had, which undoes the whole point of a careful three-state design
        // (BUGS.md #25).
        bool? value;

        if (parts.Length < 2)
        {
            value = true;
        }
        else
        {
            switch (parts[1].ToLowerInvariant())
            {
                case "on" or "true" or "yes":
                    value = true;
                    break;
                case "off" or "false" or "no":
                    value = false;
                    break;
                case "clear" or "inherit":
                    value = null;
                    break;
                default:
                    ctx.Reply(
                        $"'{parts[1]}' is not on, off, or clear.",
                        "bad");
                    return;
            }
        }

        var result = ctx.Edit(new SetRoomFlag(room.Key, flag.Key, value));

        if (!result.Success)
        {
            ctx.Reply(result.Message ?? "That flag could not be set.", "bad");
            return;
        }

        ctx.Reply(
            value switch
            {
                true => $"{flag.Key} is now on here.",
                false => $"{flag.Key} is now off here.",
                _ => $"{flag.Key} now inherits from the zone.",
            },
            "heading");
    }

    private static void ListFlags(CommandContext ctx, Room room)
    {
        var spans = new List<TextSpan> { new($"Flags for {room.Key}", "heading") };

        foreach (var flag in RoomFlags.All)
        {
            var resolved = ctx.World.ResolveFlag(room, flag);
            var origin = resolved.Source == FlagSource.Room
                ? string.Empty
                : $"  (from {resolved.Source.ToString().ToLowerInvariant()})";

            spans.Add(new TextSpan(
                $"\n  {flag.Key,-12} {(resolved.Value ? "on" : "off"),-4}{origin}",
                resolved.IsInherited ? "dim" : null));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Goto(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!RoomKey.TryParse(ctx.Argument.Trim(), out var target))
        {
            ctx.Reply("Usage: goto <world.zone.room>", "bad");
            return;
        }

        if (!ctx.World.TryGetRoom(target, out var destination))
        {
            ctx.Reply($"There is no room '{target}'.", "bad");
            return;
        }

        var origin = ctx.Actor.RoomKey;

        if (origin == target)
        {
            ctx.Reply("You are already there.", "bad");
            return;
        }

        ctx.BroadcastSight($"{ctx.Actor.Name} steps out of the world.", "movement");

        // `goto` is not a step anyone walks behind, so it ends every follow (§4.17). A builder
        // hopping across realms must not drag a party through with them.
        foreach (var dropped in ctx.World.Move(ctx.Actor, destination.Key))
        {
            dropped.SendText($"{ctx.Actor.Name} steps out of the world, and you stop following.", "bad");
        }

        ctx.BroadcastSight($"{ctx.Actor.Name} steps into the world.", "movement");

        ctx.Reply($"You step through to {target}.", "heading");
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: true);
        ctx.View.MarkRoomChanged(origin);
        ctx.View.MarkRoomChanged(destination.Key);
    }

    private static void Spawn(CommandContext ctx, SpawnTools tools)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Usage: spawn <item|mob> <template-key>, or spawn gold <amount>", "bad");
            return;
        }

        var type = parts[0].ToLowerInvariant();
        var templateKey = parts[1];

        if (type == "item")
        {
            SpawnItem(ctx, tools, templateKey);
        }
        else if (type == "mob")
        {
            SpawnMob(ctx, tools, templateKey);
        }
        else if (type == "gold")
        {
            SpawnGold(ctx, templateKey);
        }
        else
        {
            ctx.Reply("Spawn what? Use 'item', 'mob', or 'gold'.", "bad");
        }
    }

    /// <summary>
    /// Puts coin in the builder's own purse - <c>spawn gold 100</c>.
    /// </summary>
    /// <remarks>
    /// Into a purse rather than onto the floor, because gold is not an item in this game: it is a
    /// number on a character and a number on a mob, and there is no pile to walk over and pick up.
    /// The builder's own purse rather than a named target's, because <c>give 100 gold Steve</c>
    /// already exists and does the second half - which keeps this verb to the thing only a builder
    /// can do, and keeps the handing-over on the same path a player uses, testing that too.
    ///
    /// Nothing is queued for saving. Gold rides the character autosave, the same as a sale.
    /// </remarks>
    private static void SpawnGold(CommandContext ctx, string amountText)
    {
        if (!long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            ctx.Reply("Usage: spawn gold <amount>", "bad");
            return;
        }

        var character = ctx.Actor.Character;

        // Saturating, not wrapping: a builder who leans on the zero key gets an absurd purse
        // rather than a negative one, and a negative purse is a bug report from a player.
        character.Gold = amount > long.MaxValue - character.Gold
            ? long.MaxValue
            : character.Gold + amount;

        ctx.Reply($"Spawned: {amount} gold. You now have {character.Gold}.");
        ctx.BroadcastSight($"{ctx.Actor.Name} conjures a handful of coins!", "arrival");
    }

    private static void SpawnItem(CommandContext ctx, SpawnTools tools, string templateKey)
    {
        if (tools.ItemTemplates is null || tools.ItemSpawner is null)
        {
            ctx.Reply("Item spawning is not available.", "bad");
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var room = ctx.World.FindRoom(roomKey);
        if (room == null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var zone = ctx.World.FindZone(room.ZoneKey);
        var world = zone != null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone == null || world == null)
        {
            ctx.Reply("Cannot determine zone/world context.", "bad");
            return;
        }

        // Load template (this is sync, so we need to call async version differently)
        // For now, we'll do this fire-and-forget since builder commands are typed manually
        _ = SpawnItemAsync(ctx, tools, templateKey, zone, world, roomKey);
    }

    private static async System.Threading.Tasks.Task SpawnItemAsync(CommandContext ctx, SpawnTools tools, string templateKey,
        Zone zone, global::Muwbta.Domain.Worlds.World world, RoomKey roomKey)
    {
        try
        {
            var template = await tools.ItemTemplates!.GetByKeyAsync(templateKey, System.Threading.CancellationToken.None);
            if (template == null)
            {
                ctx.Reply($"Item template '{templateKey}' not found.", "bad");
                return;
            }

            var item = tools.ItemSpawner!.Spawn(template, zone, world, roomKey);
            ctx.World.AddItem(item);
            ctx.ItemSaveQueue?.Enqueue(item);

            ctx.Reply($"Spawned: {template.Icon} {template.Name}");
            ctx.BroadcastSight($"{ctx.Actor.Name} conjures {NarrationHelper.WithArticle(template.Name)}!", "arrival");
            ctx.View.MarkRoomChanged(roomKey);
        }
        catch (Exception ex)
        {
            ctx.Reply($"Error spawning item: {ex.Message}", "bad");
        }
    }

    private static void SpawnMob(CommandContext ctx, SpawnTools tools, string templateKey)
    {
        if (tools.MobTemplates is null || tools.MobSpawner is null)
        {
            ctx.Reply("Mob spawning is not available.", "bad");
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var room = ctx.World.FindRoom(roomKey);
        if (room == null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var zone = ctx.World.FindZone(room.ZoneKey);
        var world = zone != null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone == null || world == null)
        {
            ctx.Reply("Cannot determine zone/world context.", "bad");
            return;
        }

        // Load template and spawn (fire-and-forget like above)
        _ = SpawnMobAsync(ctx, tools, templateKey, zone, world, roomKey);
    }

    private static async System.Threading.Tasks.Task SpawnMobAsync(CommandContext ctx, SpawnTools tools, string templateKey,
        Zone zone, global::Muwbta.Domain.Worlds.World world, RoomKey roomKey)
    {
        try
        {
            var template = await tools.MobTemplates!.GetByKeyAsync(templateKey, System.Threading.CancellationToken.None);
            if (template == null)
            {
                ctx.Reply($"Mob template '{templateKey}' not found.", "bad");
                return;
            }

            var mob = tools.MobSpawner!.Spawn(template, zone, world, roomKey);
            ctx.World.AddMob(mob);

            var displayName = string.IsNullOrEmpty(template.Name) ? template.Key : template.Name;
            ctx.Reply($"Spawned: {template.Icon} {displayName} (level {template.Level})");
            ctx.BroadcastSight($"{ctx.Actor.Name} conjures {NarrationHelper.WithArticle(displayName)}!", "arrival");
            ctx.View.MarkRoomChanged(roomKey);
        }
        catch (Exception ex)
        {
            ctx.Reply($"Error spawning mob: {ex.Message}", "bad");
        }
    }

    private static void Despawn(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Usage: despawn <item|mob> <template-key>", "bad");
            return;
        }

        var type = parts[0].ToLowerInvariant();
        var templateKey = parts[1];

        if (type == "item")
        {
            DespawnItem(ctx, templateKey);
        }
        else if (type == "mob")
        {
            DespawnMob(ctx, templateKey);
        }
        else
        {
            ctx.Reply("Despawn what? Use 'item' or 'mob'.", "bad");
        }
    }

    /// <summary>
    /// Removes one instance of a template from the floor of this room.
    /// </summary>
    /// <remarks>
    /// Ground items only, and the owner check says so out loud rather than trusting that
    /// <see cref="World.WorldState.ItemsIn"/> happens to exclude carried ones. A builder tidying
    /// up scenery must never be able to reach into a player's pack, and "despawn item
    /// rusty-dagger" is exactly what someone would type while a player beside them is carrying
    /// one - unlike a spawn, that mistake is not undoable.
    ///
    /// One at a time, because a room with six of something is usually six deliberate placements.
    /// </remarks>
    private static void DespawnItem(CommandContext ctx, string templateKey)
    {
        var roomKey = ctx.Actor.RoomKey;

        var onTheFloor = ctx.World.ItemsIn(roomKey)
            .Where(i => i.OwnerCharacterId is null && i.ContainerItemId is null)
            .Where(i => i.TemplateKey.Equals(templateKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (onTheFloor.Count == 0)
        {
            var carried = ctx.World.OccupantsOf(roomKey)
                .SelectMany(o => ctx.World.InventoryOf(o.CharacterId))
                .Any(i => i.TemplateKey.Equals(templateKey, StringComparison.OrdinalIgnoreCase));

            ctx.Reply(
                carried
                    ? $"No '{templateKey}' is lying here - the only one in this room is being carried, and despawn does not reach into packs."
                    : $"There is no '{templateKey}' lying here.",
                "bad");
            return;
        }

        var target = onTheFloor[0];
        var displayName = target.DisplayName;

        ctx.World.RemoveItem(target);

        // The instance is a row of its own, so forgetting it in memory alone would resurrect it
        // on the next load.
        ctx.ItemSaveQueue?.EnqueueDelete(target.Id);

        var remaining = onTheFloor.Count - 1;
        ctx.Reply(
            remaining > 0
                ? $"Despawned: {displayName}. {remaining} still here."
                : $"Despawned: {displayName}.");
        ctx.BroadcastSight(
            $"{ctx.Actor.Name} banishes {NarrationHelper.WithDefiniteArticle(displayName)}.",
            "movement");
        ctx.View.MarkRoomChanged(roomKey);
    }

    /// <summary>
    /// Removes one mob of a template from this room.
    /// </summary>
    /// <remarks>
    /// A mob under a spawner's population target will simply be replaced on the next sweep -
    /// that is the spawner working, not this failing. Despawning is for the one you conjured by
    /// hand, or the one standing somewhere it should not be.
    /// </remarks>
    private static void DespawnMob(CommandContext ctx, string templateKey)
    {
        var roomKey = ctx.Actor.RoomKey;

        var here = ctx.World.MobsIn(roomKey)
            .Where(m => m.TemplateKey.Equals(templateKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (here.Count == 0)
        {
            ctx.Reply($"There is no '{templateKey}' here.", "bad");
            return;
        }

        var target = here[0];
        var displayName = target.DisplayName;

        // Take it out of any fight first. A combatant that vanishes from the world but not from
        // the combat leaves the fight two-sided forever, so whoever was swinging at it would
        // stay stuck in combat with nothing to hit.
        ctx.World.FindCombat(roomKey)?.RemoveCombatant(EntityId.ForMob(target.Id));

        ctx.World.RemoveMob(target);

        var remaining = here.Count - 1;
        ctx.Reply(
            remaining > 0
                ? $"Despawned: {displayName}. {remaining} still here."
                : $"Despawned: {displayName}.");
        ctx.Broadcast(
            $"{ctx.Actor.Name} banishes {NarrationHelper.WithDefiniteArticle(displayName)}.",
            "death");
        ctx.View.MarkRoomChanged(roomKey);
    }

    /// <summary>
    /// Unknown-verb wording rather than "you are not a builder": a player has no business
    /// learning that these commands exist.
    /// </summary>
    /// <remarks>
    /// This doc sat over <c>Spawn</c>, which is not what it describes.
    /// </remarks>
    private static bool RequireBuilder(CommandContext ctx)
    {
        if (ctx.Actor.IsBuilder)
        {
            return true;
        }

        ctx.Reply($"'{ctx.Verb}' is not something you can do. Try 'help'.", "bad");
        return false;
    }
}
