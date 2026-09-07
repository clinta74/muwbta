using System.Globalization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Quests;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Time;

namespace Muwbta.Engine.Commands;

/// <summary>
/// The command table and its handlers (PLAN.md §3.2 and Phase definitions).
/// </summary>
public sealed class CommandRegistry
{
    private readonly List<CommandDefinition> _commands;

    /// <remarks>
    /// The quest and template caches, the quest save queue, and <c>EngineOptions</c> used to be
    /// taken here so <see cref="ShopCommands"/> and <see cref="QuestCommands"/> could stash them
    /// in statics. They now travel on the <see cref="CommandContext"/> with every command, so the
    /// registry no longer needs any of them - which is also what stopped one registry's caches
    /// from serving another's world.
    /// </remarks>
    public CommandRegistry(
        AbilityCache? abilityCache = null,
        IMobTemplateRepository? mobTemplates = null,
        IItemTemplateRepository? itemTemplates = null,
        MobSpawner? mobSpawner = null,
        ItemSpawner? itemSpawner = null,
        IGameClock? clock = null,
        Domain.Abilities.Effects.EffectRegistry? effects = null)
    {
        _commands = [];
        _abilityCache = abilityCache;

        // Directions first, so a bare "n" or "e" always wins the prefix race against any
        // later verb. This is the single most-typed input in the game.
        foreach (var direction in DirectionExtensions.All)
        {
            var captured = direction;
            _commands.Add(new CommandDefinition(
                direction.ToLowerName(),
                MinLength: 1,
                Help: $"{direction.Abbreviation()} / {direction.ToLowerName()} - move {direction.ToLowerName()}",
                Handler: ctx => Move(ctx, captured)));
        }

        _commands.Add(new CommandDefinition(
            "look", 1, "look (l) - describe the room again", Look));

        _commands.Add(new CommandDefinition(
            "say", 1, "say <message> - speak to everyone in the room", Say));

        _commands.Add(new CommandDefinition(
            "who", 3, "who - list everyone online", Who));

        _commands.Add(new CommandDefinition(
            "inventory", 1, "inventory (i) - list what you're carrying", Inventory));

        _commands.Add(new CommandDefinition(
            "examine", 1, "examine <item> (ex) - look closely at an item", Examine));

        _commands.Add(new CommandDefinition(
            "get", 1, "get <item> - pick up an item from the ground", Get));

        _commands.Add(new CommandDefinition(
            "drop", 1, "drop <item> - put down an item from your inventory", Drop));

        // Full word required, for the same reason quit demands all four: nothing comes back.
        _commands.Add(new CommandDefinition(
            "destroy", 7, "destroy <item> - permanently destroy an item you're carrying", Destroy));

        _commands.Add(new CommandDefinition(
            "wear", 1, "wear <item> - equip an item on your body", Wear));

        _commands.Add(new CommandDefinition(
            "wield", 1, "wield <item> - equip an item in your hand", Wield));

        _commands.Add(new CommandDefinition(
            "remove", 2, "remove <item> (re) - unequip an item", Remove));

        _commands.Add(new CommandDefinition(
            "give", 1, "give <item> <character> - give an item to someone", Give));

        _commands.Add(new CommandDefinition(
            "emote", 1, "emote <message> - express an emotion or action", Emote));

        _commands.Add(new CommandDefinition(
            "help", 1, "help - this list", Help));

        // Four, not five. The fifth character was reserved for the admin `stat`, which is now
        // `inspect` — so `stat` reached an invisible admin verb and a player asking for their own
        // sheet by its obvious abbreviation was told the verb did not exist (RequireAdmin answers
        // as though it were unknown, deliberately, so admin verbs cannot be found by fishing).
        _commands.Add(new CommandDefinition(
            "stats", 4, "stats - show your condition, damage, armor, and combat stats", Stats));

        // Full word required: quitting by fumbling a key would be a bad surprise.
        _commands.Add(new CommandDefinition(
            "quit", 4, "quit - save and leave the world", Quit));

        CombatCommands.Register(_commands);
        RestCommands.Register(_commands);
        NutritionCommands.Register(_commands);
        AbilityCommands.Register(_commands, abilityCache, clock, effects);
        QuestCommands.Register(_commands);

        // After the quest verbs, so a bare "t" still reaches talk rather than tell. Prefix
        // matching is first-match-wins, and the older verb keeps the shorter abbreviation.
        PartyCommands.Register(_commands);
        ChannelCommands.Register(_commands);
        IgnoreCommands.Register(_commands);
        TravelCommands.Register(_commands);
        ShopCommands.Register(_commands);
        StatusCommands.Register(_commands);
        BuilderCommands.Register(_commands, mobTemplates, itemTemplates, mobSpawner, itemSpawner);
        AdminCommands.Register(_commands);
        AdminWorldCommands.Register(_commands);
    }

    /// <summary>
    /// The abilities this registry can resolve a bare verb against. Held per instance rather than
    /// in a static, for the reason the caches above already carry: a static made the
    /// last-constructed registry the one every other registry read from.
    /// </summary>
    private readonly AbilityCache? _abilityCache;

    public IReadOnlyList<CommandDefinition> Commands => _commands;

    public CommandDefinition? Find(string verb) =>
        string.IsNullOrEmpty(verb)
            ? null
            : _commands.FirstOrDefault(c => c.Matches(verb));

    /// <summary>
    /// Resolves a verb the table does not have as one of this character's abilities.
    /// </summary>
    /// <remarks>
    /// <b>Skills are verbs.</b> A player types <c>kick rat</c>, not <c>cast kick rat</c> — the
    /// reported bug was exactly that, and the answer to it.
    ///
    /// A fallback rather than thirty-seven registered verbs, for three reasons. The table is
    /// global while abilities are per-Path, so registering them would put an Adept's Amplify in
    /// front of a Temper's Ambush for the prefix <c>am</c>, and the Temper would be told they do not
    /// know an ability they have. It covers abilities added later with no further work. And
    /// because it runs only once the table has missed, <b>it can never take a command out from
    /// under someone</b> — which is what makes it safe to add to a game already being played.
    ///
    /// The cost is that an ability sharing a name with a command is unreachable as a verb, which
    /// is why the moderation verbs grew their <c>player</c> suffix rather than keeping <c>kick</c>.
    /// </remarks>
    /// <returns>
    /// The <c>cast</c> definition and the argument to run it with, or null when nothing this
    /// character knows answers to the verb — which leaves it a plain unknown command.
    /// </returns>
    public (CommandDefinition Definition, string Argument)? FindAbilityVerb(
        Character character,
        string verb,
        string argument)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (string.IsNullOrEmpty(verb) || Find("cast") is not { } cast)
        {
            return null;
        }

        var typed = string.IsNullOrWhiteSpace(argument) ? verb : $"{verb} {argument}";

        // Resolved here rather than left to the handler, so a genuine typo still reads as an
        // unknown command instead of "you don't know an ability called 'flurgle rat'".
        return AbilityLookup.Resolve(_abilityCache, character, typed).Found
            ? (cast, typed)
            : null;
    }

    /// <summary>
    /// Punctuation that stands in for a whole verb, with no space after it.
    /// </summary>
    /// <remarks>
    /// The two commands worth the keystroke are the two typed most often and always with an
    /// argument, so <c>'hello</c> and <c>;grins</c> read naturally. Expanded here rather than
    /// registered as verbs, because a <see cref="CommandDefinition"/> is matched by prefix and
    /// would demand the space - <c>' hello</c> works, <c>'hello</c> would not.
    ///
    /// Deliberately a short list. Every character claimed here is one that can never start an
    /// ordinary command, and a shortcut nobody remembers costs more than it saves.
    /// </remarks>
    private static readonly Dictionary<char, string> _verbShortcuts = new()
    {
        ['\''] = "say",
        ['"'] = "say",
        [';'] = "emote",
        [':'] = "emote",
    };

    /// <summary>
    /// Splits raw input into a verb and its remainder. "say  hello  there" keeps the
    /// message intact - only the separator after the verb is consumed.
    /// </summary>
    public static (string Verb, string Argument) Split(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (_verbShortcuts.TryGetValue(trimmed[0], out var shortcut))
        {
            return (shortcut, trimmed[1..].TrimStart());
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0
            ? (trimmed.ToLowerInvariant(), string.Empty)
            : (trimmed[..space].ToLowerInvariant(), trimmed[(space + 1)..].TrimStart());
    }

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    private static void Move(CommandContext ctx, Direction direction)
    {
        var character = ctx.Actor.Character;

        // Can't move while in active combat
        if (character.CombatState == CombatState.Fighting)
        {
            ctx.Reply("You can't leave while in combat! Try 'flee' to escape.", "bad");
            return;
        }

        // Can't move while resting or sleeping
        if (RestGate.Refuse(character) is { } resting)
        {
            ctx.Reply(resting, "bad");
            return;
        }

        // Nor while spent. What you are carrying decides how much you owe before you can walk
        // again - the one rule in the game that reads item weight.
        if (ExhaustionGate.Refuse(character, ctx.World, ctx.ItemTemplates) is { } spent)
        {
            ctx.Reply(spent, "bad");
            return;
        }

        // Snared. This mostly matters out of combat - in a fight the check above has already
        // refused - but a root that let you walk away the moment the fight ended would be a
        // strange kind of snare.
        if (ctx.World.IsRooted(character.Id, ctx.Clock?.CurrentPulse ?? 0L))
        {
            var holding = ctx.World.RootName(character.Id, ctx.Clock?.CurrentPulse ?? 0L) ?? "something";
            ctx.Reply($"You cannot go anywhere — you are {holding}.", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        var exit = room?.ExitTo(direction);

        if (exit is null)
        {
            ctx.Reply($"You cannot go {direction.ToLowerName()} from here.", "bad");
            return;
        }

        // Asked before the destination is resolved, so a locked door reads as locked whether or
        // not there is anything behind it yet (§4.15). Builders are not exempt: `goto` already
        // takes them anywhere, so nothing is lost by letting them meet their own gates the way a
        // player will - which is the only way an author finds out the flag key has a typo in it.
        if (ExitGate.Refuse(ctx.World, character, exit) is { } barred)
        {
            ctx.Reply(barred, "bad");
            return;
        }

        if (!ctx.World.TryGetRoom(exit.ToRoomKey, out var destination))
        {
            // A dangling exit: the builder linked it before creating the target, or deleted
            // the target later. Fails closed rather than throwing on the loop (PLAN.md §7.4).
            //
            // A builder gets the offer to materialize it instead - that is what turns walking
            // into the fastest way to lay out geography (§7.6). A player must never learn that
            // the world can be incomplete here.
            if (ctx.Actor.IsBuilder)
            {
                ctx.Reply(
                    $"There is no room {direction.ToLowerName()} yet ('{exit.ToRoomKey}'). "
                    + $"Type 'dig {direction.ToLowerName()}' to create it.",
                    "bad");
            }
            else
            {
                ctx.Reply("The way is blocked.", "bad");
            }

            return;
        }

        var origin = ctx.Actor.RoomKey;

        ctx.BroadcastSight($"{ctx.Actor.Name} leaves {direction.ToLowerName()}.", "movement");
        // The one relocation in the game that does not break following, because it is the only one
        // anybody could have walked behind (§4.17).
        ctx.World.Move(ctx.Actor, destination.Key, walked: true);
        ctx.BroadcastSight($"{ctx.Actor.Name} arrives from the {direction.Opposite().ToLowerName()}.", "movement");

        ctx.Reply($"You walk {direction.ToLowerName()}.", "movement");
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: false);

        // Both rooms changed occupancy, so both maps need redrawing for everyone in them.
        ctx.View.MarkRoomChanged(origin);
        ctx.View.MarkRoomChanged(destination.Key);

        // Last, so the leader's own arrival is on screen before anyone else's. Each follower
        // refreshes the two rooms again, which is the price of them being ordinary movers.
        FollowSystem.Step(
            ctx.World, ctx.View, ctx.Actor, origin, direction, ctx.Clock?.CurrentPulse ?? 0L);
    }

    private static void Look(CommandContext ctx) =>
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: true);

    private static void Say(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Say what?", "bad");
            return;
        }

        if (ctx.RefusedForMute() || ctx.RefusedForLanguage(ctx.Argument))
        {
            return;
        }

        ctx.Reply($"You say, '{ctx.Argument}'", "speech");
        ctx.Broadcast($"{ctx.Actor.TaggedName} says, '{ctx.Argument}'", "speech", speech: true);
    }

    private static void Who(CommandContext ctx)
    {
        var players = ctx.World.AllPlayers
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var spans = new List<TextSpan>
        {
            new($"{players.Count} {(players.Count == 1 ? "player" : "players")} online", "heading"),
        };

        foreach (var player in players)
        {
            var status = player.IsLinkDead ? " [link-dead]" : string.Empty;
            spans.Add(new TextSpan(
                $"\n  {player.TaggedName}  level {player.Character.Level} {player.Character.Path}{status}"));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>
    /// What an emote line opens with. The MUD convention, and a character no name can contain.
    /// </summary>
    internal const string EmoteMarker = "* ";

    /// <summary>
    /// Verbs of speech and address that, at the head of an emote, would make it read as a tell,
    /// a say, or a system line. Matched on the first word only, case-insensitively, with the
    /// trailing "s" so that "tell" and "tells" both count.
    /// </summary>
    private static readonly string[] SpeechOpeners =
    [
        "say", "says", "tell", "tells", "ask", "asks", "whisper", "whispers", "shout", "shouts",
        "yell", "yells", "chat", "chats", "reply", "replies", "announce", "announces",
    ];

    private static readonly char[] TrailingPunctuation = [',', '.', ':', ';', '!', '?', '\'', '"'];

    private static bool OpensLikeSpeech(string argument)
    {
        // The split is on whitespace, so "says," arrives with its comma - and "emote says, '...'"
        // is the forgery, not "emote says '...'". Punctuation off the end before the lookup.
        var (first, _) = CommandText.SplitFirstWord(argument, lowercase: true);
        return SpeechOpeners.Contains(first.TrimEnd(TrailingPunctuation), StringComparer.Ordinal);
    }

    private void Help(CommandContext ctx)
    {
        var spans = new List<TextSpan> { new("Commands", "heading") };

        // Directions collapse to one line; listing all six adds nothing.
        spans.Add(new TextSpan("\n  n / e / s / w / u / d - move between rooms"));

        foreach (var command in _commands.Where(
            c => !IsDirection(c.Name) && !c.Hidden && c.VisibleTo(ctx.Actor.Role)))
        {
            spans.Add(new TextSpan($"\n  {command.Help}"));
        }

        spans.Add(new TextSpan("\n\nMost verbs accept a prefix, so 'l' is 'look'.", "dim"));

        // A shortcut nobody discovers is not a shortcut. Listed from the same table the parser
        // reads, so adding one here is impossible to forget.
        var shortcuts = string.Join(
            ", ",
            _verbShortcuts
                .GroupBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(group => $"{string.Join(" or ", group.Select(p => p.Key))} = {group.Key}"));

        spans.Add(new TextSpan($"\nNo space needed after {shortcuts}.", "dim"));

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Quit(CommandContext ctx)
    {
        ctx.Reply("You gather your things and step out of the world.", "heading");
        ctx.BroadcastSight($"{ctx.Actor.Name} leaves the world.", "movement");
        ctx.LeaveRequested = LeaveReason.Quit;
    }

    private static void Inventory(CommandContext ctx)
    {
        var items = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var equipped = items.Where(i => i.EquippedSlot is not null).ToList();
        var unequipped = items.Where(i => i.EquippedSlot is null).ToList();

        var spans = new List<TextSpan>();

        // Always, and every slot - not only the filled ones.
        //
        // <b>The empty lines are how the slot list is learned.</b> Showing only what was worn
        // made a slot nobody had filled yet invisible, so a player wearing six things had no way
        // to discover that Feet and Trinket exist at all: a whole equipment category findable
        // only by happening to pick one up and read its usage line. The list is fixed and short,
        // so printing it whole costs eight lines and answers "what else could I be wearing".
        spans.Add(new TextSpan("You are wearing/wielding:", "heading"));

        // Worn items are never collapsed, however many share a name: they are listed under their
        // slots and the slot is the information. Two identical rings in two places is two facts,
        // not one fact twice (§4.14).
        var bySlot = equipped
            .Where(i => i.EquippedSlot is not null)
            .ToLookup(i => i.EquippedSlot!.Value);

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            var worn = bySlot[slot].ToList();

            if (worn.Count == 0)
            {
                spans.Add(new TextSpan($"\n  [{slot}] "));
                spans.Add(new TextSpan("empty", "dim"));
                continue;
            }

            foreach (var item in worn)
            {
                spans.Add(new TextSpan($"\n  [{slot}] {item.DisplayName}"));

                // Worn quest items are tagged too. A quest reward with a slot is ordinary
                // content, and it would be odd for the tag to disappear the moment it is put on.
                if (ItemState.IsQuestItem(item))
                {
                    spans.Add(new TextSpan(" (q)", "dim"));
                }

                // What it is doing for you, on the screen that lists what you are wearing.
                // `stats` has carried this since the bonuses block was fixed; the pack listing
                // is where somebody deciding what to keep is actually looking.
                if (ItemStatLine.For(item.ResolvedStats) is { } line)
                {
                    spans.Add(new TextSpan($"\n    {line}", "dim"));
                }
            }
        }

        if (unequipped.Count > 0)
        {
            // Always preceded by the equipment block above, which always prints.
            spans.Add(new TextSpan("\n\nYou are carrying:", "heading"));

            // One line per kind of thing carried, with a count (§4.14). A pack holding six stones
            // is six lines of "stone" otherwise, which pushes everything else off the screen and
            // reads as a bug. The collapse is a sentence, not a data structure: nothing about the
            // instances merges, and get / drop / sell / examine still act on one item.
            foreach (var group in unequipped
                .GroupBy(i => (Name: i.DisplayName, Quest: ItemState.IsQuestItem(i)))
                .OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase))
            {
                spans.Add(new TextSpan($"\n  {group.Key.Name}"));

                // Marked in the list rather than only on examine. A quest item cannot be sold or
                // destroyed, so a player wondering why the shopkeeper refuses it should be able
                // to see the reason in the same breath as the refusal - and a pack with three
                // things in it that will not shift is worth being able to read at a glance.
                //
                // One parenthesis carries both, because two brackets in a row would read as two
                // separate facts about two separate things.
                var count = group.Count();

                if (count > 1 && group.Key.Quest)
                {
                    spans.Add(new TextSpan($" (x{count} q)", "dim"));
                }
                else if (count > 1)
                {
                    spans.Add(new TextSpan($" (x{count})", "dim"));
                }
                else if (group.Key.Quest)
                {
                    spans.Add(new TextSpan(" (q)", "dim"));
                }
            }
        }

        if (items.Count == 0)
        {
            // Appended rather than sent instead of the slot list. This used to return
            // here, discarding the spans built above - which meant the one player who most
            // needs to be told there are eight slots, the one who has filled none of them,
            // was the only player never shown them.
            spans.Add(new TextSpan("\n\nYou aren't carrying anything.", "dim"));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Examine(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Examine what?", "bad");
            return;
        }

        // Look in hand first, then on the ground, then at whoever is standing here. "examine" is
        // most often aimed at something you are already holding - but it used to look at nothing
        // else at all, so examining an NPC in front of you reported that it was not there.
        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument)
            ?? FindItemByName(ctx.World.ItemsIn(ctx.Actor.RoomKey), ctx.Argument);

        if (targetItem is not null)
        {
            ExamineItem(ctx, targetItem);
            return;
        }

        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(ctx.Actor.RoomKey), ctx.Argument, m => m.TemplateName, m => m.TemplateKey);

        if (targetMob is not null)
        {
            ExamineMob(ctx, targetMob);
            return;
        }

        ctx.Reply($"You don't see any {ctx.Argument} here.", "bad");
    }

    private static void ExamineItem(CommandContext ctx, ItemInstance item)
    {
        var template = ctx.ItemTemplates?.Get(item.TemplateKey);
        var article = NarrationHelper.WithDefiniteArticle(item.DisplayName);

        var spans = new List<TextSpan>
        {
            new($"You examine {article}.", "heading"),
        };

        var description = template?.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            spans.Add(new TextSpan($"\n{description}"));
        }

        spans.Add(new TextSpan($"\n{UsageProse(template)}", "dim"));

        if (ItemState.IsQuestItem(item))
        {
            // Which of the two it is, because they are different situations for the player.
            // The line said "it cannot be sold or destroyed" of both, which was the wrong
            // half of the truth for the commoner one: a quest item nobody is waiting for is
            // disposable, and a player who reads otherwise carries it forever.
            var spokenFor = QuestBinding.SpokenFor(
                ctx.Quests, ctx.World, ctx.Actor.CharacterId, item.TemplateKey);

            spans.Add(new TextSpan(
                spokenFor is not null
                    ? $"\n{spokenFor.Name} is waiting for this — it cannot be sold or destroyed."
                    : "\nIt is bound to a quest, so no shop will take it — but no quest of yours "
                      + "is waiting for it, and you could destroy it.",
                "dim"));
        }

        AppendBuilderDetail(ctx, spans, item.TemplateKey, BuilderLinks.ToItem(item.TemplateKey), () =>
        [
            ("value", item.Value.ToString(CultureInfo.InvariantCulture)),
            ("weight", template?.Weight.ToString(CultureInfo.InvariantCulture) ?? "?"),
            ("slots", template is null || template.Slots.Count == 0
                ? "none"
                : string.Join("/", template.Slots) + (template.IsTwoHanded ? " (two-handed)" : string.Empty)),
            ("speed", template?.AttackDelayPulses?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            ("verb", template?.AttackVerb ?? "—"),
            ("stats", Describe(item.ResolvedStats)),
        ]);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void ExamineMob(CommandContext ctx, Mob mob)
    {
        var template = ctx.MobTemplates?.Get(mob.TemplateKey);
        var displayName = MobLabel.For(ctx.World, mob);
        var article = NarrationHelper.WithDefiniteArticle(displayName);

        var spans = new List<TextSpan>
        {
            new($"You examine {article}.", "heading"),
        };

        if (!string.IsNullOrWhiteSpace(template?.Description))
        {
            spans.Add(new TextSpan($"\n{template.Description}"));
        }

        // Named rather than pronouned, in every line below. These sentences used to run "It is
        // unhurt." straight into "They are not someone you can fight." - two pronouns for one
        // mob, a line apart. Choosing between them does not work either: "It" is wrong for the
        // old man and the bar maiden, "They" is wrong for a rat, and nothing in a template says
        // which kind of thing it is. The subject reads for both, and it is unambiguous when two
        // mobs are standing in the room.
        var subject = NarrationHelper.WithDefiniteArticle(displayName, capitalize: true);

        // Health as a fraction rather than a number: a player should be able to see that
        // something is hurt without being handed the combat log.
        spans.Add(new TextSpan($"\n{ConditionProse(mob, subject, article)}", "dim"));

        var disposition = MobBehavior.DispositionOf(template?.Behavior);
        if (disposition == MobDisposition.Npc)
        {
            spans.Add(new TextSpan($"\n{subject} is not someone you can fight.", "dim"));
        }
        else if (disposition == MobDisposition.Aggressive)
        {
            spans.Add(new TextSpan(
                $"\n{subject} watches you the way something decides to attack.", "bad"));
        }

        if (MobBehavior.IsShopkeeper(template?.Behavior))
        {
            spans.Add(new TextSpan($"\n{subject} keeps a shop — try 'list'.", "good"));
        }

        AppendBuilderDetail(ctx, spans, mob.TemplateKey, BuilderLinks.ToMob(mob.TemplateKey), () =>
        [
            ("level", mob.Level.ToString(CultureInfo.InvariantCulture)),
            ("health", $"{mob.Vitals.Health}/{mob.Vitals.HealthMax}"),
            ("xp", mob.ResolvedXp.ToString(CultureInfo.InvariantCulture)),
            ("gold", mob.ResolvedGold.ToString(CultureInfo.InvariantCulture)),
            ("type", MobBehavior.NameOf(disposition)),
            ("spawner", mob.SpawnerId?.ToString() ?? "—"),
            ("stats", Describe(mob.ResolvedStats)),
        ]);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>
    /// How badly hurt something looks, without printing its hit points.
    /// </summary>
    /// <param name="subject">
    /// The mob named with its definite article and a capital — "The old man". Passed in rather
    /// than pronouned, because no pronoun is right for both a bar maiden and a rat.
    /// </param>
    /// <param name="midSentence">
    /// The same name for use inside a sentence — "the old man". A separate argument rather than
    /// a <c>ToLower</c> of <paramref name="subject"/>, which would turn a proper name into
    /// "mira".
    /// </param>
    private static string ConditionProse(Mob mob, string subject, string midSentence)
    {
        if (mob.Vitals.HealthMax <= 0)
        {
            // The leading "It" here is the impersonal one — about the difficulty of telling,
            // not about the mob — so it survives.
            return $"It is hard to tell what shape {midSentence} is in.";
        }

        var fraction = (double)mob.Vitals.Health / mob.Vitals.HealthMax;

        return fraction switch
        {
            >= 1.0 => $"{subject} is unhurt.",
            >= 0.75 => $"{subject} has been scratched.",
            >= 0.5 => $"{subject} is bleeding.",
            >= 0.25 => $"{subject} is badly hurt.",
            > 0 => $"{subject} is barely standing.",
            _ => $"{subject} is not long for this world.",
        };
    }

    /// <summary>
    /// Appends the template key, the raw numbers, and a link into the builder.
    /// </summary>
    /// <remarks>
    /// Builders only. A player has no use for a template key and no way to follow the link, and
    /// showing it would put the builder's existence on the wire for everyone. The values are
    /// gathered lazily so a player's <c>examine</c> does no work to produce a block it will
    /// never see.
    /// </remarks>
    private static void AppendBuilderDetail(
        CommandContext ctx,
        List<TextSpan> spans,
        string templateKey,
        string builderPath,
        Func<IReadOnlyList<(string Label, string Value)>> details)
    {
        if (!ctx.Actor.Role.Satisfies(AccountRole.Builder))
        {
            return;
        }

        // The key *is* the link. It is already on screen and it is already the thing the builder
        // would go looking for, so a separate "Open in builder" line was one row of pure
        // scaffolding under every block.
        //
        // The line break goes in a plain span, never in the link. A link renders as an
        // inline-block <button> and the scrollback's white-space: pre-wrap is inherited into it,
        // so a newline inside one makes the *button* several lines tall instead of breaking the
        // line, and its label drops to the baseline of whatever preceded it.
        spans.Add(new TextSpan("\n\n"));
        spans.Add(new TextSpan($"[{templateKey}]", "link", builderPath));

        var rows = details();

        // Padded to the widest label in *this* block rather than to a constant, so the columns
        // stay aligned when a field is added and no block is indented further than it needs to
        // be. The scrollback is monospace with pre-wrap, so the spaces survive to the screen -
        // without the padding, "xp:" and "spawner:" start their values seven columns apart.
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Label.Length);

        foreach (var (label, value) in rows)
        {
            spans.Add(new TextSpan($"\n  {(label + ":").PadRight(width + 2)}{value}", "dim"));
        }
    }

    /// <summary>A stat bag as one readable line, or an em dash when it is empty.</summary>
    private static string Describe(IReadOnlyDictionary<string, object>? stats) =>
        stats is null || stats.Count == 0
            ? "—"
            : string.Join(", ", stats.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Prose telling a player, in-world, how (or whether) an item can be equipped.</summary>
    /// <remarks>
    /// The three hand cases are answered before the per-slot lines, because "either hand" and
    /// "both hands" are what a player most needs off this screen and neither is a slot — they are
    /// the shape of the list. Everything else is still one slot and reads as it always did.
    /// </remarks>
    private static string UsageProse(ItemTemplate? template)
    {
        var slots = SlotRules.Normalize(template?.Slots);

        if (slots.Count == 0)
        {
            return "It isn't something you can wear or wield.";
        }

        if (SlotRules.ClaimsBothHands(template))
        {
            return "It takes both hands — there is no wielding anything alongside it.";
        }

        var hands = SlotRules.HandSlots(template);
        if (hands.Count == 2)
        {
            return "It is balanced for either hand — you could wield it in whichever is free.";
        }

        return NarrationHelper.List([.. slots.Select(SlotLine)]);
    }

    private static string SlotLine(ItemSlot slot) => slot switch
    {
        ItemSlot.Head => "It looks made to be worn on your head.",
        ItemSlot.Chest => "It looks made to be worn over your chest.",
        ItemSlot.Hands => "It looks made to be worn on your hands.",
        ItemSlot.Legs => "It looks made to guard your legs.",
        ItemSlot.Feet => "It looks made to be worn on your feet.",
        ItemSlot.MainHand => "It has the balance of something meant for your main hand — you could wield it.",
        ItemSlot.OffHand => "It sits ready for your off hand — you could wield it.",
        ItemSlot.Trinket => "It looks like it would make a fine trinket.",
        _ => "It isn't something you can wear or wield.",
    };

    private static void Get(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Get what?", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere.", "bad");
            return;
        }

        var items = ctx.World.ItemsIn(ctx.Actor.RoomKey);
        var targetItem = FindItemByName(items, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"There is no {ctx.Argument} here.", "bad");
            return;
        }

        // Asked first, because it is the question of whether this is theirs to touch at all -
        // ahead of lore, which is a question about what they may end up holding.
        if (LootClaim.Refuse(
                targetItem,
                ctx.Actor.CharacterId,
                ctx.Clock?.UtcNow ?? DateTimeOffset.UtcNow,
                NarrationHelper.WithDefiniteArticle(targetItem.DisplayName, capitalize: true))
            is { } claimed)
        {
            ctx.Reply(claimed, "bad");
            return;
        }

        if (ItemRules.IsLore(ctx.ItemTemplates, targetItem.TemplateKey) &&
            ItemRules.AlreadyHolds(ctx.World, ctx.Actor.CharacterId, targetItem.TemplateKey))
        {
            ctx.Reply(
                $"You already carry {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}, "
                + "and one is all anyone gets.",
                "bad");
            return;
        }

        // Both marks come off as it changes hands, not left to lapse. A claim that survived the
        // pickup would come back the moment the item was put down again, so a party's share could
        // be banked for good by taking it and dropping it, and the person it was dropped for would
        // be refused. A decay stamp that survived would be worse: already past, it would delete
        // the item the instant it touched the floor again.
        LootClaim.Clear(targetItem);
        GroundDecay.Clear(targetItem);

        ctx.World.PickUpItem(targetItem, ctx.Actor.CharacterId);
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You take {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} takes {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
    }

    private static void Drop(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Drop what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        // Asked before the bound check and worded exactly as `destroy` words it, because these
        // four verbs are the four ways an item leaves a pack and a player should not have to learn
        // which of them cares (BUGS.md #8).
        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply(
                $"You'll have to remove {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)} first.",
                "bad");
            return;
        }

        if (ItemRules.IsNoDrop(ctx.ItemTemplates, targetItem.TemplateKey))
        {
            // The way out is 'destroy', and saying so is the whole point - a bound item with no
            // stated way to be rid of it is a slot in the pack the player cannot reason about.
            ctx.Reply(
                $"{NarrationHelper.WithDefiniteArticle(targetItem.DisplayName, capitalize: true)} "
                + "will not leave your hand. You could destroy it, if you truly meant to.",
                "bad");
            return;
        }

        ctx.World.DropItem(targetItem, ctx.Actor.RoomKey);

        // Deleted rather than saved, and the row is the whole reason.
        //
        // Nothing ever reads an item row back except a character's own inventory at login
        // (GameEndpoints, filtered on OwnerCharacterId), so a row with a room key is written and
        // never looked at again. Saving one did not make the item survive a restart - it does not
        // - it only left a row behind, for ever, every time anybody put anything down.
        //
        // The delete is still doing the necessary half of what the save did: releasing ownership.
        // Left alone, the row would go on naming the dropper and their next login would load the
        // item straight back into their pack, quietly undoing the drop.
        //
        // So an item on the floor lives until somebody takes it or the server restarts. If
        // stashing is ever wanted it should be a thing a player is given - a bank, or a room they
        // own - rather than a side effect of every dropped torch being immortal in Postgres.
        ctx.ItemSaveQueue?.EnqueueDelete(targetItem.Id);

        ctx.Reply($"You drop {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} drops {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
    }

    /// <summary>
    /// Takes an item out of the world for good.
    /// </summary>
    /// <remarks>
    /// Drop leaves something to pick back up; this does not. The two losses a player would most
    /// regret are refused outright rather than merely warned about - what they are wearing, which
    /// they can only have destroyed by mistyping, and an item a quest they are <em>on</em> is
    /// counting, which would strand that quest.
    ///
    /// <b>Only a quest they are on.</b> The flag alone used to refuse, which protected the ledger
    /// of a quest the character had never met and, in the Path-gated chains, could not take - and
    /// an epic reward is a quest item and no-drop and Path-locked at once, so a Temper holding an
    /// Adept stormrod had a pack slot nothing could ever be done about. <c>QuestBinding</c> asks
    /// the narrower question.
    ///
    /// The shopkeeper still refuses every quest item, and that asymmetry is deliberate: destroying
    /// is disposal and selling is profit, so relaxing the counter as well would make any quest item
    /// a thing to farm and vendor.
    /// </remarks>
    private static void Destroy(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Destroy what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.DisplayName);

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You'll have to remove {article} first.", "bad");
            return;
        }

        if (QuestBinding.RefuseDestroy(
                ctx.Quests, ctx.World, ctx.Actor.CharacterId, targetItem, article) is { } bound)
        {
            ctx.Reply(bound, "bad");
            return;
        }

        // Out of the world and out of storage. Forgetting it in memory alone would hand it back
        // on the next load, still owned by the player who destroyed it.
        ctx.World.RemoveItem(targetItem);
        ctx.ItemSaveQueue?.EnqueueDelete(targetItem.Id);

        ctx.Reply($"You destroy {article}. It is gone for good.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} destroys {article}.", "movement");
    }

    private static void Wear(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Wear what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You're already wearing {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.DisplayName);
        var template = ctx.ItemTemplates?.Get(targetItem.TemplateKey);
        var candidates = SlotRules.WornSlots(template);

        if (candidates.Count == 0)
        {
            // "wear" is for armour and trinkets; hands are wielded, not worn. Sending someone to
            // the right verb is friendlier than silently jamming a sword onto their chest.
            ctx.Reply(
                SlotRules.HandSlots(template).Count > 0
                    ? $"You can't wear {article} — try wielding it instead."
                    : $"You can't wear {article} — it isn't something you can equip.",
                "bad");
            return;
        }

        if (ChooseSlot(ctx, candidates, article, "wearing") is not { } slot)
        {
            return;
        }

        TryEquip(ctx, targetItem, slot, "wear", "wears");
    }

    private static void Wield(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Wield what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You're already wielding {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.DisplayName);
        var template = ctx.ItemTemplates?.Get(targetItem.TemplateKey);
        var candidates = SlotRules.HandSlots(template);

        if (candidates.Count == 0)
        {
            // The mirror of Wear: only hand slots are wielded; armour is worn.
            ctx.Reply(
                SlotRules.IsEquippable(template)
                    ? $"You can't wield {article} — try wearing it instead."
                    : $"You can't wield {article} — it isn't something you can equip.",
                "bad");
            return;
        }

        // A weapon that needs both hands is refused before the hands are searched, because the
        // free hand it would otherwise fall through to is the one it is about to claim.
        if (SlotRules.ClaimsBothHands(template) && OccupantOf(ctx, ItemSlot.OffHand) is { } inTheWay)
        {
            ctx.Reply(
                $"You need both hands for {article}, and your off hand is holding "
                + $"{NarrationHelper.WithDefiniteArticle(inTheWay.DisplayName)}.",
                "bad");
            return;
        }

        if (ChooseSlot(ctx, candidates, article, "wielding") is not { } slot)
        {
            return;
        }

        // The other side of the same rule: nothing joins a hand the main hand has already claimed.
        if (slot == ItemSlot.OffHand
            && OccupantOf(ctx, ItemSlot.MainHand) is { } heldMain
            && SlotRules.ClaimsBothHands(ctx.ItemTemplates?.Get(heldMain.TemplateKey)))
        {
            ctx.Reply(
                $"{NarrationHelper.WithDefiniteArticle(heldMain.DisplayName, capitalize: true)} "
                + "takes both hands — there is no off hand free.",
                "bad");
            return;
        }

        if (!TryEquip(ctx, targetItem, slot, "wield", "wields"))
        {
            return;
        }

        // Striking with a second weapon is trained, not innate. Say so, or an untrained
        // character reads their off hand doing nothing as a bug.
        //
        // Only for something that could strike. A shield, a torch, anything whose template
        // declares no attack delay never swings however trained you are (`AttackDelayPulses`), so
        // blaming the training would name a fix that does not exist - which is what the `stats`
        // screen has always said correctly one screen over.
        if (slot == ItemSlot.OffHand &&
            template?.AttackDelayPulses is not null &&
            !CanStrikeWithOffHand(ctx.Actor.Character))
        {
            ctx.Reply(
                $"You settle {article} into your off hand, though you've not the training to strike with it.",
                "dim");
        }
    }

    private static bool CanStrikeWithOffHand(Character character) =>
        AbilityProgression.KnowsPassive(character.Path, character.Level, PassiveKeys.DualWield);

    /// <summary>
    /// Reports the off hand only when there is something to report, and is honest when it is
    /// carrying rather than fighting - a weapon that shows a damage range it will never roll is
    /// exactly the kind of lie the stats screen was rewritten to stop telling.
    /// </summary>
    private static void AppendOffHand(
        CommandContext ctx,
        List<TextSpan> spans,
        Character character,
        IReadOnlyList<ItemInstance> equipped)
    {
        var offHand = equipped.FirstOrDefault(i => i.EquippedSlot == ItemSlot.OffHand);
        if (offHand is null)
        {
            return;
        }

        var displayName = offHand.DisplayName;
        var template = ctx.ItemTemplates?.Get(offHand.TemplateKey);

        spans.Add(new TextSpan($"\n\nOff Hand: {displayName}", "heading"));

        if (template?.AttackDelayPulses is null)
        {
            spans.Add(new TextSpan("\n  Not a weapon — it never strikes.", "dim"));
            return;
        }

        if (!CanStrikeWithOffHand(character))
        {
            spans.Add(new TextSpan("\n  Carried only — you've not the training to strike with it.", "dim"));
            return;
        }

        var share = AbilityProgression.OffHandDamageShare(character.Path, character.Level);
        var offAttack = EquipmentResolver.ResolveAttackerStatsForHand(
            character.Level, character.Attributes.MightModifier, equipped, ItemSlot.OffHand, share);
        var delay = AttackTiming.Clamp(template.AttackDelayPulses);
        var independent = AbilityProgression.KnowsPassive(
            character.Path, character.Level, PassiveKeys.Ambidextrous);

        spans.Add(new TextSpan(
            $"\n  Damage Range: {offAttack.MinDamage + offAttack.BaseDamage}-{offAttack.MaxDamage + offAttack.BaseDamage}"));
        spans.Add(new TextSpan($"\n  Speed: one swing every {delay * 0.25:0.##}s"));
        spans.Add(new TextSpan(
            independent
                ? "\n  Ambidextrous — strikes on its own timing."
                : "\n  Follows your main hand.",
            "dim"));
    }

    /// <summary>Whatever is equipped in a slot right now, or null if it is free.</summary>
    private static ItemInstance? OccupantOf(CommandContext ctx, ItemSlot slot) =>
        ctx.World.InventoryOf(ctx.Actor.CharacterId).FirstOrDefault(i => i.EquippedSlot == slot);

    /// <summary>
    /// The first free slot among the ones an item declares, or null having explained why there
    /// is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>First free rather than asking.</b> The candidates arrive in enum order, which puts the
    /// main hand ahead of the off hand, so an either-hand blade reaches for the main hand and
    /// settles for the other - and a bare verb still always does something, which is how every
    /// other command in the game behaves.
    /// </para>
    /// <para>
    /// When nothing is free the refusal names every occupant rather than the first, because with
    /// one candidate that is the message this always gave and with two "your main hand" alone
    /// would read as an invitation to free it.
    /// </para>
    /// </remarks>
    private static ItemSlot? ChooseSlot(
        CommandContext ctx,
        IReadOnlyList<ItemSlot> candidates,
        string article,
        string gerund)
    {
        foreach (var slot in candidates)
        {
            if (OccupantOf(ctx, slot) is null)
            {
                return slot;
            }
        }

        var occupied = candidates
            .Select(slot => $"your {SlotRules.Name(slot)} for "
                + NarrationHelper.WithDefiniteArticle(OccupantOf(ctx, slot)!.DisplayName))
            .ToList();

        ctx.Reply(
            $"You have nowhere to put {article} — you're already using {NarrationHelper.List(occupied)}.",
            "bad");

        return null;
    }

    /// <summary>
    /// Equips into the item's own slot, refusing if that slot is already filled. Returns false
    /// (having replied) when the slot is taken, so callers can bail without repeating the check.
    /// </summary>
    private static bool TryEquip(
        CommandContext ctx,
        ItemInstance item,
        ItemSlot slot,
        string selfVerb,
        string othersVerb)
    {
        // Here rather than in Wear and Wield separately: they are two verbs onto one action, and
        // a restriction enforced in one of them is a restriction you get around by typing the
        // other word.
        if (ItemRules.RefusePath(
                ctx.ItemTemplates,
                item.TemplateKey,
                ctx.Actor.Character.Path,
                NarrationHelper.WithDefiniteArticle(item.DisplayName, capitalize: true)) is { } wrongPath)
        {
            ctx.Reply(wrongPath, "bad");
            return false;
        }

        var occupant = ctx.World.InventoryOf(ctx.Actor.CharacterId)
            .FirstOrDefault(i => i.EquippedSlot == slot);

        if (occupant is not null)
        {
            var occupantName = NarrationHelper.WithDefiniteArticle(occupant.DisplayName);
            ctx.Reply($"You're already using your {SlotRules.Name(slot)} for {occupantName}.", "bad");
            return false;
        }

        var article = NarrationHelper.WithDefiniteArticle(item.DisplayName);
        ctx.World.EquipItem(item, slot);
        ctx.ItemSaveQueue?.Enqueue(item);

        ctx.Reply($"You {selfVerb} {article}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} {othersVerb} {article}.", "movement");
        return true;
    }

    private static void Remove(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Remove what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is null)
        {
            ctx.Reply($"You're not wearing {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "bad");
            return;
        }

        ctx.World.UnequipItem(targetItem);
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You remove {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} removes {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "movement");
    }

    private static void Give(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Give what to whom?", "bad");
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Give what to whom?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var (itemName, targetName) = SplitGive(ctx, parts, inventory);

        var targetItem = FindItemByName(inventory, itemName);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {itemName}.", "bad");
            return;
        }

        // Check for NPC quest turn-in first (PLAN.md §5.2b)
        if (QuestCommands.TryTurnInQuest(ctx, targetItem.TemplateKey, targetName))
        {
            return;
        }

        var targetPlayer = ctx.World.FindPlayerByName(targetName);
        if (targetPlayer is null)
        {
            // A mob standing right here is not "no one". Only players and quest turn-ins can be
            // handed anything, and the turn-in above has already declined, so the honest answer
            // is that this one does not want it - not that nobody of that name is present, which
            // is what a player reads as "I typed the name wrong".
            if (NameMatch.Best(
                    ctx.World.MobsIn(ctx.Actor.RoomKey),
                    targetName,
                    m => m.TemplateName,
                    m => m.TemplateKey) is { } mob)
            {
                ctx.Reply(RefuseGiftTo(ctx, mob, targetItem), "bad");
                return;
            }

            ctx.Reply($"There is no one named {targetName} here.", "bad");
            return;
        }

        if (targetPlayer.CharacterId == ctx.Actor.CharacterId)
        {
            ctx.Reply("You can't give items to yourself.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply(
                $"You'll have to remove {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)} first.",
                "bad");
            return;
        }

        // Checked after the quest turn-in above, not before: handing a bound item to the person
        // the chain wants it handed to is the one transfer that must always work.
        if (ItemRules.IsNoDrop(ctx.ItemTemplates, targetItem.TemplateKey))
        {
            ctx.Reply(
                $"{NarrationHelper.WithDefiniteArticle(targetItem.DisplayName, capitalize: true)} "
                + "is yours and will not go to anyone else.",
                "bad");
            return;
        }

        // The recipient's rule, not the giver's. Lore is about who ends up holding it, so it has
        // to be asked on the receiving side - otherwise two players hand one back and forth and
        // each ends the exchange holding a second copy they could not have picked up.
        if (ItemRules.IsLore(ctx.ItemTemplates, targetItem.TemplateKey) &&
            ItemRules.AlreadyHolds(ctx.World, targetPlayer.CharacterId, targetItem.TemplateKey))
        {
            ctx.Reply($"{targetPlayer.Name} already has one, and one is all anyone gets.", "bad");
            return;
        }

        ctx.World.PickUpItem(targetItem, targetPlayer.CharacterId);
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You give {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)} to {targetPlayer.Name}.", "good");
        targetPlayer.SendText($"{ctx.Actor.Name} gives you {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} gives {NarrationHelper.WithDefiniteArticle(targetItem.DisplayName)} to {targetPlayer.Name}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
    }

    /// <summary>
    /// Third-person prose, shown to the room and to the person who wrote it.
    /// </summary>
    /// <remarks>
    /// The echo used to be missing, so typing <c>;grins</c> produced nothing at all on your own
    /// screen and there was no way to tell a working emote from a swallowed one — in an empty
    /// room, no way to tell at all.
    ///
    /// The same third-person line rather than a second-person rewrite, because rewriting means
    /// conjugating: "grins" → "grin" is easy, and there is no rule that also turns "waves at the
    /// fire" or "is lost in thought" into something that reads. Showing what everyone else sees is
    /// both honest and the thing a player wants to check.
    /// </remarks>
    private static void Emote(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Emote what?", "bad");
            return;
        }

        // Covered too: an emote is free text in front of other players, so a mute that let it
        // through would be a mute you could talk around.
        if (ctx.RefusedForMute())
        {
            return;
        }

        // An emote is the one verb whose free text follows the name directly, so without a
        // guard `emote tells you, 'send me your password'` produced "Kael tells you, 'send me
        // your password'" - the exact shape of a tell, in a different colour, and colour is not
        // something a player reads under pressure. Two defences: the line opens with a marker no
        // other verb produces, and text that begins with a speech verb is refused outright.
        if (ctx.RefusedForLanguage(ctx.Argument))
        {
            return;
        }

        if (OpensLikeSpeech(ctx.Argument))
        {
            ctx.Reply("An emote is something you do. To speak, use say or tell.", "bad");
            return;
        }

        var line = $"{EmoteMarker}{ctx.Actor.TaggedName} {ctx.Argument}";

        ctx.Reply(line, "emote");

        // An emote is something you do where people can see it, and a sleeping player is not
        // somebody who can. Speech is deliberately not filtered the same way - `say` is addressed
        // at the room and being woken by it is the point of shouting at somebody.
        ctx.BroadcastSight(line, "emote", speech: true);
    }

    private static void Stats(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var items = ctx.World.InventoryOf(character.Id);
        var equipped = items.Where(i => i.EquippedSlot is not null).ToList();

        // Read through the same resolver combat uses. This screen used to derive its numbers
        // from level with its own formula, so it reported a damage range the game would never
        // roll - the reason a sword could show 4-12 here while landing 3s in a fight.
        var attack = EquipmentResolver.ResolveAttackerStatsForHand(
            character.Level,
            character.Attributes.MightModifier,
            equipped,
            ItemSlot.MainHand,
            offHandShare: 1m);

        var defense = EquipmentResolver.ResolveDefenderStats(
            character.Level,
            character.Attributes.AgilityModifier,
            equipped);

        // What the dice actually produce, modifier included, before the target's armour.
        var minDamage = attack.MinDamage + attack.BaseDamage;
        var maxDamage = attack.MaxDamage + attack.BaseDamage;

        var mainHand = equipped.FirstOrDefault(i => i.EquippedSlot == ItemSlot.MainHand);
        var mainDelay = AttackTiming.Clamp(
            mainHand is null ? null : ctx.ItemTemplates?.Get(mainHand.TemplateKey)?.AttackDelayPulses);

        // An opponent of the player's own level carrying nothing, which is what both percentages
        // above are quoted against. Neither attack nor defence has a meaning on its own now that
        // landing a blow is a ratio between the two - only a meaning against somebody.
        var peer = new DefenderStats(Level: character.Level, DefenseRating: 0, Armor: 0);
        var peerAttack = new AttackerStats(
            Level: character.Level,
            AttackRating: (int)Math.Round(
                DamageCalculator.MobSkill * character.Level, MidpointRounding.AwayFromZero),
            BaseDamage: 0,
            MinDamage: 1,
            MaxDamage: 1);

        // Hunger and thirst are said out loud once, on the tick that crosses a threshold, and
        // never again (`NeedsSystem`) - so a player who was looking elsewhere, or who logged in
        // already hungry, had nowhere to ask. The only other thing the needs do is slow recovery,
        // and that is a multiplier nothing in the game prints. This is the screen you look
        // yourself up on, so it answers in both directions: "fed and watered" is an answer, and
        // silence is not.
        var vitals = character.Vitals;
        var condition = Needs.Describe(vitals.Hunger, vitals.Thirst);
        var regenShare = Needs.RegenShare(vitals.Hunger, vitals.Thirst);

        var spans = new List<TextSpan>
        {
            new($"Combat Stats", "heading"),
            new($"\nLevel: {character.Level}"),
            new($"\nGold: {character.Gold:N0}"),
            new(
                $"\nCondition: {condition ?? "fed and watered"}",
                condition is null ? "good" : "bad"),
            new($"\nDamage Range: {minDamage}-{maxDamage}", "good"),
            new($"\n  Dice: {attack.MinDamage}-{attack.MaxDamage}, Might bonus: {attack.BaseDamage:+#;-#;+0}"),
            new($"\n  Speed: one swing every {mainDelay * 0.25:0.##}s"),
            new($"\nAttack Rating: {attack.AttackRating}", "good"),
            new($"\n  Lands {DamageCalculator.HitChance(attack, peer):P0} of swings against an even match"),
            // Evasion whole rather than as its parts - it is what an attacker is measured against, and
            // the level term inside it is not something the player can act on. Both percentages name an
            // even match because neither number means anything alone any more: hit chance is a ratio, so
            // what a guard is worth depends on who is swinging (PLAN.md 4.6).
            new($"\nDefense: {DamageCalculator.Evasion(defense):0}", "good"),
            new($"\n  Evaded by {1 - DamageCalculator.HitChance(peerAttack, defense):P0} of an even match's swings"),
            new($"\n  Armour: {defense.Armor} rating, absorbs {ArmorCurve.Mitigation(defense.Armor, character.Level):P0} of an even match's blows"),
        };

        // Only when it costs something. At worst recovery is two fifths of normal, and a line
        // reading "100%" on every healthy sheet would be one more number to read past.
        if (regenShare < 1.0)
        {
            spans.Add(new TextSpan($"\n  Recovering at {regenShare:P0} of your normal rate", "dim"));
        }

        AppendOffHand(ctx, spans, character, equipped);

        if (equipped.Count > 0)
        {
            spans.Add(new TextSpan($"\n\nEquipped Bonuses:", "heading"));
            foreach (var item in equipped.OrderBy(i => i.EquippedSlot))
            {
                var slot = item.EquippedSlot?.ToString() ?? "Unknown";
                var displayName = item.DisplayName;
                // Every stat the item carries, not the ones whose key happens to contain
                // "Multiplier". Across content that filter admitted `damageMultiplier` and hid
                // `armor`, `bonus` and `defense` — so every armour piece printed under a heading
                // promising bonuses and then showed nothing (BUGS.md #11). `examine` never
                // filtered, which is why the builder view was right and only the player's was
                // wrong.
                // Through ItemStatLine rather than Describe, which is the other half of that same
                // mistake: having started printing the whole bag, this printed it the way a
                // builder reads one - `bonus=4, damageMax=13, damageMin=7`, alphabetical, so
                // the maximum came before the minimum and the one term that is accuracy
                // rather than damage was named only by its authored key.
                var statsStr = ItemStatLine.For(item.ResolvedStats);

                spans.Add(new TextSpan($"\n  [{slot}] {displayName}"));
                if (!string.IsNullOrEmpty(statsStr))
                {
                    spans.Add(new TextSpan($"\n    {statsStr}", "dim"));
                }
            }
        }

        AppendBuilderSheet(ctx, spans, equipped);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>
    /// The builder's half of the stats screen: where you are, what you are wearing by key, and
    /// a way into the editor for each.
    /// </summary>
    /// <remarks>
    /// A builder testing a change is usually standing in the thing they just edited, so the most
    /// useful link on this screen is the room itself - it saves finding the room again in a tree
    /// that may be several zones deep.
    /// </remarks>
    private static void AppendBuilderSheet(
        CommandContext ctx,
        List<TextSpan> spans,
        IReadOnlyList<ItemInstance> equipped)
    {
        if (!ctx.Actor.Role.Satisfies(AccountRole.Builder))
        {
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var zone = ctx.World.FindZone(roomKey.ZoneKey);

        spans.Add(new TextSpan("\n\nBuilder", "heading"));

        // Same column treatment as the examine block. Each key carries its own link, so there is
        // no trailing list of "Open X in builder" lines repeating keys already in the column.
        var rows = new List<(string Label, string Value, string? Link)>
        {
            ("room", roomKey.ToString(), BuilderLinks.ToRoom(roomKey)),
        };

        if (zone is not null)
        {
            // The dial this room's contents are scaled by. Reading it here saves guessing why a
            // rat in this zone hits harder than the same rat next door.
            // "0.##" so a neutral dial reads "×1" rather than "×1.0" - a column of ".0" is noise
            // that makes the two factors a builder actually changed harder to pick out.
            var m = zone.Multipliers;
            rows.Add(("zone", zone.Key, null));
            rows.Add((
                "scaling",
                $"strength ×{m.Strength:0.##}  damage ×{m.Damage:0.##}  " +
                $"xp ×{m.Xp:0.##}  gold ×{m.Gold:0.##}",
                null));
        }

        foreach (var item in equipped.OrderBy(i => i.EquippedSlot))
        {
            rows.Add((
                item.EquippedSlot?.ToString() ?? "held",
                item.TemplateKey,
                BuilderLinks.ToItem(item.TemplateKey)));
        }

        var width = rows.Max(r => r.Label.Length);

        foreach (var (label, value, link) in rows)
        {
            // The label and its padding are a plain span, so neither the line break nor the
            // alignment ever lands inside a link - see AppendBuilderDetail for what that costs.
            spans.Add(new TextSpan($"\n  {(label + ":").PadRight(width + 2)}", "dim"));
            spans.Add(link is null
                ? new TextSpan(value, "dim")
                : new TextSpan(value, "link", link));
        }

    }

    /// <summary>
    /// Decides where the item name ends and the recipient begins in <c>give a b c</c>.
    /// </summary>
    /// <remarks>
    /// Both halves can be several words — "empty glass", "bar maiden" — and there is no
    /// separator, so the split has to be *found* rather than assumed. This used to take the first
    /// two whitespace-separated words and throw the rest away: <c>give empty glass maiden</c>
    /// became item "empty", recipient "glass", and answered "There is no one named glass here."
    /// while silently ignoring the word that named the actual recipient. <c>give beer old man</c>
    /// worked only because "old" happens to prefix-match the old man.
    ///
    /// Every split point is tried, shortest item name first, and the first one where *both*
    /// halves resolve wins. Left to right because the item is the half the player is more likely
    /// to abbreviate — they are holding it, and they can see the recipient's full name in the
    /// room.
    ///
    /// When nothing resolves, the split chosen is the one that produces the most useful
    /// complaint: the *longest* item name that still resolves, so the leftover recipient is as
    /// small as the input allows. Given "give empty glass bar maiden" with nobody there, the
    /// shortest match would complain about "glass bar maiden" — technically the truth, and
    /// useless. And failing even that, everything-but-the-last-word as the item, which is how a
    /// player reading "You don't have purple banana" would have meant it.
    /// </remarks>
    private static (string ItemName, string TargetName) SplitGive(
        CommandContext ctx, string[] parts, IReadOnlyList<ItemInstance> inventory)
    {
        (string, string)? itemFound = null;

        for (var i = 1; i < parts.Length; i++)
        {
            var candidate = (
                string.Join(' ', parts[..i]),
                string.Join(' ', parts[i..]));

            if (FindItemByName(inventory, candidate.Item1) is null)
            {
                continue;
            }

            if (RecipientExists(ctx, candidate.Item2))
            {
                return candidate;
            }

            // Kept rather than kept-first: the last one to get here is the longest item name
            // that resolves, which leaves the smallest possible leftover to complain about.
            itemFound = candidate;
        }

        return itemFound
            ?? (string.Join(' ', parts[..^1]), parts[^1]);
    }

    /// <summary>
    /// Why a mob will not take the thing you are holding out to it.
    /// </summary>
    /// <remarks>
    /// Two answers rather than one, because the two situations want different things from the
    /// player. A mob mixed up in quests at all is one you should be talking to — the usual reason
    /// a turn-in is declined is that the quest is not active yet, which no amount of holding the
    /// item out will fix. A mob in no quest whatsoever is simply not a recipient, and saying
    /// "try talking" there would send the player off to have a conversation that does not exist.
    ///
    /// Named rather than pronouned, for the reason <c>examine</c> is: no pronoun is right for
    /// both a bar maiden and a rat.
    /// </remarks>
    private static string RefuseGiftTo(CommandContext ctx, Mob mob, ItemInstance item)
    {
        var who = NarrationHelper.WithDefiniteArticle(mob.DisplayName, capitalize: true);
        var what = NarrationHelper.WithDefiniteArticle(item.DisplayName);

        var inQuests = ctx.Quests is not null
            && (ctx.Quests.GetByGiverMobKey(mob.TemplateKey).Count > 0
                || ctx.Quests.GetByTurninMobKey(mob.TemplateKey).Count > 0);

        return inQuests
            ? $"{who} has no use for {what} right now. Try talking first."
            : $"{who} has no use for {what}.";
    }

    /// <summary>
    /// Whether anything in the room answers to this name — a player, or a mob.
    /// </summary>
    /// <remarks>
    /// Deliberately side-effect free, unlike <c>TryTurnInQuest</c>, which resolves a recipient
    /// and hands the item over in one step. Choosing where to split the argument has to be able
    /// to ask the question without answering it.
    /// </remarks>
    private static bool RecipientExists(CommandContext ctx, string name) =>
        ctx.World.FindPlayerByName(name) is not null
        || NameMatch.Best(
            ctx.World.MobsIn(ctx.Actor.RoomKey), name, m => m.TemplateName, m => m.TemplateKey)
            is not null;

    /// <summary>
    /// The item in this list that the player meant.
    /// </summary>
    /// <remarks>
    /// This demanded the whole display name or the whole key, so an "old coin" answered to
    /// "old coin" and "old-coin" and nothing else - <c>get coin</c> failed on the only thing in
    /// the room. See <see cref="NameMatch"/> for what counts as a match now.
    ///
    /// The doc above used to sit over <c>SplitGive</c>, where a second <c>summary</c> followed it
    /// immediately — so the compiler discarded this one and the method it describes had none.
    /// </remarks>
    private static ItemInstance? FindItemByName(IEnumerable<ItemInstance> items, string name) =>
        NameMatch.Best(items, name, i => i.TemplateName, i => i.TemplateKey);

    private static bool IsDirection(string name) =>
        DirectionExtensions.All.Any(d => d.ToLowerName() == name);
}
