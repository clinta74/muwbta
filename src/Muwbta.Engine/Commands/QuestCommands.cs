using System.Text.RegularExpressions;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Quests;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Commands;

/// <summary>
/// Talking to quest givers, the journal, and turn-in (PLAN.md §4.9, §5.2b).
/// </summary>
/// <remarks>
/// The cache and save queue come from the <see cref="CommandContext"/> rather than from statics
/// captured at registration, for the reason set out on <see cref="ShopCommands"/>: a static made
/// the last-constructed registry the one every other registry read from.
/// </remarks>
public static class QuestCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "talk", 1, "talk <npc> [what you say] (t) - speak to someone; answer a giver to take a quest", Talk));

        // The same verb with an "about" in it, because that is how the sentence is said. Three
        // characters, so `as` keeps reaching assist.
        commands.Add(new CommandDefinition(
            "ask", 3, "ask <npc> about <topic> - ask someone what they know", Ask));

        // "quest" goes in FIRST, and the order is load-bearing. A verb matches on any prefix of
        // its name, and Find takes the first definition that matches - so with "quests" ahead of
        // it, typing `quest` matched *quests* ("quests".StartsWith("quest")) and QuestDetail had
        // no reachable input at all. It was dead from the day it was written.
        //
        // What keeps the other prefix pairs safe is that the longer verb demands more characters
        // than the shorter one has: `whois` needs 5, so "who" can never reach it. "quests" asking
        // for only 3 is what broke the symmetry.
        //
        // `stats` was the other example here, held off "stat" by demanding five. That was the
        // wrong fix and it has been undone: the four-character form belonged to the player's own
        // screen, and reserving it for an admin verb only meant `stat` answered "not something you
        // can do". Guard a pair by naming them apart, not by making the common one harder to type.
        commands.Add(new CommandDefinition(
            "quest", 3, "quest [name] - your journal, or one quest in detail", QuestDetail));

        commands.Add(new CommandDefinition(
            "quests", 3, "quests - list your active quests", Quests));

        commands.Add(new CommandDefinition(
            "abandon", 3, "abandon <name> - give up an active quest", Abandon));
    }

    /// <summary>
    /// Speaks to somebody in the room (PLAN.md §4.9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two shapes. <c>talk vane</c> asks what they have to say; <c>talk vane cogs</c> answers
    /// them, and answering a quest giver is how a quest is taken on.
    /// </para>
    /// <para>
    /// <b>Talking no longer starts anything by itself.</b> It used to begin every quest the giver
    /// had available, which meant there was no way to <em>read</em> what somebody wanted without
    /// taking the job — and with all 35 quests authored <c>autoStart: false</c>, this verb is how
    /// the entire game progresses, so that was the only reading anyone got.
    /// </para>
    /// <para>
    /// <b>And it is for everybody now, not only givers.</b> A mob with no quests answered
    /// <em>"has nothing to say to you about quests"</em>, which names the subsystem rather than
    /// the world; a shopkeeper said it while standing behind a counter. <c>talk</c> is registered
    /// at one character, which is too good a verb to point at one subsystem.
    /// </para>
    /// </remarks>
    private static void Talk(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Talk to whom?");
            return;
        }

        TalkTo(ctx, ctx.Argument);
    }

    /// <summary>
    /// <c>ask adda about the stone</c>: <c>talk</c> with the word "about" taken out, so a topic
    /// (<see cref="MobTopic"/>) can be reached by the sentence a person would actually say.
    /// </summary>
    private static void Ask(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Ask whom about what?");
            return;
        }

        // Only the first "about", and only as a whole word: a mob called "the man about town"
        // keeps his name, and "ask adda about about" still asks her about "about".
        var argument = Regex.Replace(
            ctx.Argument, @"\s+about\s+", " ", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

        TalkTo(ctx, argument.Trim());
    }

    private static void TalkTo(CommandContext ctx, string argument)
    {
        var character = ctx.Actor.Character;
        var (targetMob, said) = FindTarget(ctx.World.MobsIn(character.RoomKey), argument);

        if (targetMob is null)
        {
            ctx.Reply($"You don't see '{argument}' here.");
            return;
        }

        if (said is null)
        {
            Greet(ctx, targetMob);
        }
        else
        {
            Answer(ctx, targetMob, said);
        }
    }

    /// <summary>
    /// Splits <c>talk &lt;who&gt; [what]</c> into the mob addressed and what was said to them.
    /// </summary>
    /// <remarks>
    /// <b>The whole argument is tried as a name first</b>, so a mob called "bar maiden" wins over
    /// "bar" plus the word "maiden". Only then are split points walked, longest name first, and the
    /// first one that names somebody standing here takes the rest as speech — the same
    /// find-the-split rule <c>give &lt;item&gt; &lt;recipient&gt;</c> uses, for the same reason:
    /// both halves can be several words and neither length is knowable in advance.
    ///
    /// Unlike that one, the right half does not have to resolve to anything. "I will get you your
    /// widgets" is a sentence, not a keyword, and it should still reach the quest called Widgets.
    /// </remarks>
    private static (Mob? Mob, string? Said) FindTarget(IReadOnlyList<Mob> mobs, string argument)
    {
        Mob? ByName(string name) =>
            NameMatch.Best(mobs, name, m => m.TemplateName, m => m.TemplateKey);

        if (ByName(argument) is { } whole)
        {
            return (whole, null);
        }

        for (var split = argument.LastIndexOf(' ');
             split > 0;
             split = argument.LastIndexOf(' ', split - 1))
        {
            var name = argument[..split].TrimEnd();
            var rest = argument[(split + 1)..].Trim();

            if (rest.Length > 0 && ByName(name) is { } found)
            {
                return (found, rest);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// What somebody says when spoken to: their quests, their counter, or their own line.
    /// </summary>
    private static void Greet(CommandContext ctx, Mob mob)
    {
        var character = ctx.Actor.Character;
        var lines = new List<IReadOnlyList<TextSpan>>();
        var unlinked = new List<Quest>();

        // What answering this mob could start right now, which is exactly the set Answer will
        // resolve against. Computed once here so the link written into the offer is checked
        // against the same candidates that will decide where a click lands.
        var available = Available(ctx, mob, character);

        foreach (var quest in OfferedBy(ctx, mob))
        {
            var questState = ctx.World.GetQuestState(character.Id, quest.Key);

            // A quest whose content has been deleted is dormant: it stops being offered, and
            // anyone already holding it is told so rather than left hunting for an item that no
            // longer exists (PLAN.md §7.4).
            if (IsDormant(ctx, quest))
            {
                if (questState?.Status == QuestStatus.Active)
                {
                    lines.Add(Prose(
                        $"About {quest.Name} — that business is closed for now. Hold on to what you have."));
                }

                continue;
            }

            // Not for this Path: not offered at all, and if they are somehow holding it, told why
            // rather than left to discover it at the turn-in.
            if (WrongPathNote(quest, character) is { } wrongPath)
            {
                if (questState?.Status == QuestStatus.Active)
                {
                    lines.Add(Prose(wrongPath));
                }

                continue;
            }

            if (questState is null && CheckPrerequisites(ctx.World, character.Id, quest))
            {
                // Offered, not begun. The one line in this method that used to have a side effect.
                Offer(ctx, mob, quest, available, lines, unlinked);
            }
            else if (questState?.Status == QuestStatus.Active)
            {
                lines.Add(Prose(quest.Dialogue.TryGetValue(QuestDialogue.GiverInProgress, out var inProgress)
                    ? inProgress
                    : $"Still working on {quest.Name}?"));
            }
            else if (questState?.Status == QuestStatus.Completed && !quest.IsRepeatable)
            {
                lines.Add(Prose(quest.Dialogue.TryGetValue(QuestDialogue.GiverComplete, out var complete)
                    ? complete
                    : $"You've already completed {quest.Name}."));
            }
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable
                && ChainStillRunning(ctx, character.Id, quest.Key) is { } blocking)
            {
                // Repeatable, finished, but something further down the chain is still open. Taking
                // it again now would reset a step whose consequences are still in play.
                lines.Add(Prose($"About {quest.Name} — finish {blocking.Name} first, or give it up."));
            }
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable
                && !UpstreamHasRunAgain(ctx, character.Id, quest, questState))
            {
                // Repeatable and clear below, but the step *above* has not been run again - so
                // this is a player re-entering the chain in the middle. Left open, the old man
                // would hand out the second errand to somebody with no glass and no way to get
                // one, because taking the first errand is then blocked by this one being active.
                // Recoverable by abandoning, but a chain should be re-entered at its head.
                lines.Add(Prose($"About {quest.Name} — that comes later. First things first."));
            }
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable)
            {
                Offer(ctx, mob, quest, available, lines, unlinked);
            }

            // Anything left over is a quest whose prerequisites are unmet, and it is simply not
            // mentioned.
        }

        var topics = OpenTopics(ctx, mob);
        var linkedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (lines.Count == 0)
        {
            // The greeting may mark words that are topics, rendered as links the way an offer's
            // marked words are - and checked the same way, so a marker that leads nowhere reads
            // as prose rather than as a button that does nothing.
            lines.Add(Rendered(ctx, mob, SmallTalk(ctx, mob), topics, available, linkedTopics));
        }

        foreach (var line in lines)
        {
            ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(line)));
        }

        // How to say yes, for any quest whose own prose could not carry the link. Dim, and after
        // everything else, because it is stage direction rather than something anybody says.
        foreach (var quest in unlinked)
        {
            Affordance(ctx, mob, quest, available);
        }

        // And what else they could be asked, for any topic the lines above did not already offer.
        // The same stage direction as the quest affordance, for the same reason: a topic nobody
        // can discover is a line nobody hears.
        TopicAffordance(ctx, mob, topics, available, linkedTopics);
    }

    /// <summary>
    /// The topics this character may ask <paramref name="mob"/> about right now: every one the
    /// template carries whose gates are satisfied (<see cref="MobTopic.IsOpenTo"/>).
    /// </summary>
    private static IReadOnlyList<MobTopic> OpenTopics(CommandContext ctx, Mob mob)
    {
        var character = ctx.Actor.Character;
        var behavior = ctx.MobTemplates?.Get(mob.TemplateKey)?.Behavior;

        return [.. MobBehavior.TopicsOf(behavior).Where(topic => topic.IsOpenTo(
            character,
            key => ctx.World.GetQuestState(character.Id, key)?.Status == QuestStatus.Completed))];
    }

    /// <summary>The topic a word asks for, among those open, or null.</summary>
    private static MobTopic? TopicFor(IReadOnlyList<MobTopic> topics, string word) =>
        topics.FirstOrDefault(topic => topic.AnswersTo(word));

    /// <summary>
    /// A line somebody says, with any marked word that names an open topic rendered as the
    /// command that asks about it. Used for greetings and for topic answers, so one topic can
    /// lead to the next.
    /// </summary>
    /// <remarks>
    /// The same round trip <see cref="Offer"/> makes for quest links, and for the same reason: a
    /// link is only shipped if feeding its command back through <see cref="FindTarget"/> lands on
    /// this mob and this topic, and on no quest - quests answer first, so a word both could take
    /// would go to the errand and the link would lie.
    /// </remarks>
    private static IReadOnlyList<TextSpan> Rendered(
        CommandContext ctx,
        Mob mob,
        string text,
        IReadOnlyList<MobTopic> topics,
        IReadOnlyList<Quest> available,
        HashSet<string> linkedTopics)
    {
        var spans = new List<TextSpan>();

        foreach (var segment in QuestOffer.Parse(text))
        {
            if (segment.IsLink
                && TopicFor(topics, segment.Text) is { } topic
                && TopicCommand(ctx, mob, topic, topics, available) is { } command)
            {
                spans.Add(new TextSpan(segment.Text, null, C: command));
                linkedTopics.Add(topic.Keyword);
                continue;
            }

            spans.Add(new TextSpan(segment.Text));
        }

        return spans;
    }

    /// <summary>
    /// The command that asks <paramref name="mob"/> about <paramref name="topic"/>, or null when
    /// no way of addressing them makes the word land there.
    /// </summary>
    private static string? TopicCommand(
        CommandContext ctx,
        Mob mob,
        MobTopic topic,
        IReadOnlyList<MobTopic> topics,
        IReadOnlyList<Quest> available)
    {
        foreach (var address in Addresses(mob))
        {
            var command = $"talk {address} {topic.Keyword}".ToLowerInvariant();

            var (who, said) = FindTarget(
                ctx.World.MobsIn(ctx.Actor.Character.RoomKey), command["talk ".Length..]);

            if (ReferenceEquals(who, mob)
                && said is not null
                && Match(available, said) is null
                && TopicFor(topics, said) == topic)
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// A dim line naming the topics still unasked, each clickable, after everything else.
    /// </summary>
    private static void TopicAffordance(
        CommandContext ctx,
        Mob mob,
        IReadOnlyList<MobTopic> topics,
        IReadOnlyList<Quest> available,
        HashSet<string> linkedTopics)
    {
        var spans = new List<TextSpan>();

        foreach (var topic in topics.Where(t => !linkedTopics.Contains(t.Keyword)))
        {
            if (TopicCommand(ctx, mob, topic, topics, available) is not { } command)
            {
                continue;
            }

            spans.Add(new TextSpan(spans.Count == 0 ? "(You could ask about '" : "', '", "dim"));
            spans.Add(new TextSpan(command, "dim", C: command));
        }

        if (spans.Count == 0)
        {
            return;
        }

        spans.Add(new TextSpan("'.)", "dim"));
        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>A line nobody can click: one plain span, which is what most lines are.</summary>
    private static IReadOnlyList<TextSpan> Prose(string text) => [new TextSpan(text)];

    /// <summary>
    /// Adds the giver's offer, with the words they marked rendered as something to click.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The marker is authored into the prose (<see cref="QuestOffer"/>), so the link sits on the
    /// noun the errand is about rather than in a parenthetical bolted onto the end. An offer with
    /// no markers — or one whose markers do not survive the check below — falls back to that
    /// parenthetical, which is why 35 quests kept working the day this landed and could be
    /// re-authored one at a time.
    /// </para>
    /// <para>
    /// <b>Every link is round-tripped before it is sent.</b> The command is fed back through the
    /// same <see cref="FindTarget"/> and <see cref="Match"/> the player's own typing goes through,
    /// and it is only rendered if it comes back holding this mob and this quest. A link that would
    /// start the wrong quest is therefore not a thing that can be shipped: the worst an ambiguous
    /// marker can do is degrade to the parenthetical.
    /// </para>
    /// </remarks>
    private static void Offer(
        CommandContext ctx,
        Mob mob,
        Quest quest,
        IReadOnlyList<Quest> available,
        List<IReadOnlyList<TextSpan>> lines,
        List<Quest> unlinked)
    {
        var text = OfferText(quest);
        var segments = QuestOffer.Parse(text);
        var spans = new List<TextSpan>();
        var linked = false;

        foreach (var segment in segments)
        {
            if (!segment.IsLink)
            {
                spans.Add(new TextSpan(segment.Text));
                continue;
            }

            if (Command(ctx, mob, quest, available, segment.Text) is not { } command)
            {
                // The marked words do not lead back here. Keep them as prose - the sentence still
                // reads - and let the parenthetical say how to take it on.
                spans.Add(new TextSpan(segment.Text));
                continue;
            }

            spans.Add(new TextSpan(segment.Text, null, C: command));
            linked = true;
        }

        lines.Add(spans);

        if (!linked)
        {
            unlinked.Add(quest);
        }
    }

    /// <summary>
    /// The command that says yes to this quest using <paramref name="phrase"/>, or null when no
    /// way of addressing this mob makes those words land here.
    /// </summary>
    private static string? Command(
        CommandContext ctx, Mob mob, Quest quest, IReadOnlyList<Quest> available, string phrase)
    {
        var said = phrase.Trim();

        if (said.Length == 0)
        {
            return null;
        }

        foreach (var address in Addresses(mob))
        {
            var command = $"talk {address} {said}".ToLowerInvariant();

            var (who, spoken) = FindTarget(
                ctx.World.MobsIn(ctx.Actor.Character.RoomKey), command["talk ".Length..]);

            if (ReferenceEquals(who, mob) && spoken is not null && Match(available, spoken) == quest)
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// The line that says how to take a quest on, with the command itself clickable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The clickable part is the command, verbatim.</b> The client echoes what it sends, so a
    /// span that read one thing and ran another would put a string in the player's transcript they
    /// never typed — and keeping them identical is also what stops the label and the action
    /// drifting apart, which is what authoring the link inside the prose would have risked.
    /// </para>
    /// <para>
    /// The words around it stay plain text, so a client that renders no links — the phone, a
    /// screen reader, anything reading the raw stream — still shows a complete instruction rather
    /// than a sentence with a hole in it.
    /// </para>
    /// </remarks>
    private static void Affordance(
        CommandContext ctx, Mob mob, Quest quest, IReadOnlyList<Quest> available)
    {
        // The same round trip the inline links get, over the words worth suggesting: a cue drawn
        // from the name, then the whole name, then the key, which cannot fail to identify it.
        var command = new[] { Cue(quest), quest.Name, quest.Key }
            .Select(phrase => Command(ctx, mob, quest, available, phrase))
            .FirstOrDefault(c => c is not null);

        if (command is null)
        {
            return;
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(
        [
            new TextSpan($"({quest.Name} — '", "dim"),
            // Styled dim like the words around it: a client that renders no commands shows one
            // uniform sentence rather than a stray highlight, and the button carries its own look.
            new TextSpan(command, "dim", C: command),
            new TextSpan("' to take it on.)", "dim"),
        ])));
    }

    /// <summary>
    /// Answers somebody. Against a quest giver that is how a quest is accepted.
    /// </summary>
    /// <remarks>
    /// Matched through <see cref="NameMatch"/> over the quest's name and key, which already ranks
    /// an exact name above a last word above any word — so "The Scraped Plates" answers to
    /// <c>plates</c>, to <c>scraped</c>, and to the whole title, with no keyword field to author
    /// and nothing new to keep in step. A sentence works too: the last word of "I will fetch your
    /// plates" is the one that matches.
    /// </remarks>
    private static void Answer(CommandContext ctx, Mob mob, string said)
    {
        var character = ctx.Actor.Character;
        var available = Available(ctx, mob, character);

        // What they said whole, then its words longest first. Whole first so an exact title or an
        // exact key lands at rank 0 or 1 and cannot be beaten by a stray word; words after, so a
        // sentence reaches the quest the way a bare keyword does, without a special case.
        foreach (var word in Spoken(said))
        {
            if (Match(available, word) is { } quest)
            {
                var timesCompleted =
                    ctx.World.GetQuestState(character.Id, quest.Key)?.TimesCompleted ?? 0;

                Begin(ctx, character.Id, quest, timesCompleted);

                // The instruction, not the pitch a second time: the offer is what talked them
                // into it and they have just read it. This is also what the giver repeats when
                // asked again later, which is what makes coming back a way to remember the job.
                ctx.Reply(Instruction(quest));
                ctx.Reply($"You take on {quest.Name}.", "good");
                ReplyNeeds(ctx, quest);
                return;
            }
        }

        // Nothing matched. If they named something they are already on, say that rather than
        // pretending not to understand - it is the likeliest reason a word stops working.
        foreach (var word in Spoken(said))
        {
            var held = OfferedBy(ctx, mob).FirstOrDefault(q =>
                NameMatch.Matches(word, q.Name, q.Key)
                && ctx.World.GetQuestState(character.Id, q.Key)?.Status == QuestStatus.Active);

            if (held is not null)
            {
                ctx.Reply($"You are already on {held.Name}.");
                return;
            }
        }

        // Not an errand, so perhaps a question. After the quests, deliberately: an errand's word
        // is a commitment and a topic's is a curiosity, and the validator refuses a template that
        // gives both the same word so this order is never the difference.
        var topics = OpenTopics(ctx, mob);

        foreach (var word in Spoken(said))
        {
            if (TopicFor(topics, word) is { } topic)
            {
                var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topic.Keyword };

                ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(
                    Rendered(ctx, mob, topic.Text, topics, available, linked))));
                return;
            }
        }

        ctx.Reply($"{mob.DisplayName} does not know what you mean by that.");
    }

    /// <summary>
    /// The quests this mob could start for this character right now.
    /// </summary>
    /// <remarks>
    /// One definition, read by <see cref="Answer"/> to decide where a word lands and by
    /// <see cref="Offer"/> to check that a link it is about to render lands there too. Two copies
    /// of this predicate would be two answers to "which quest does this word mean", and the one
    /// the player gets would be the one that was not checked.
    /// </remarks>
    private static List<Quest> Available(CommandContext ctx, Mob mob, Character character) =>
        [.. OfferedBy(ctx, mob)
            .Where(q => !IsDormant(ctx, q)
                && WrongPathNote(q, character) is null
                && Startable(ctx, character.Id, q))];

    /// <summary>
    /// The quest a word means, if any: what its giver marked in the prose, then its name and key.
    /// </summary>
    /// <remarks>
    /// Authored markers are tried first because they were written on purpose, and a word derived
    /// from a title is a guess by comparison. Both are needed: the marker is how the link in the
    /// prose works, and the name is how somebody typing at the prompt gets there without having
    /// read the exact wording.
    /// </remarks>
    private static Quest? Match(IReadOnlyList<Quest> available, string needle)
    {
        var said = needle.Trim();

        var marked = available
            .Where(quest => QuestOffer.Keywords(OfferText(quest))
                .Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToList();

        // Exactly one, or none. Two errands marking the same word is a word that means neither -
        // taking the first would give one of them the link and leave the other's identical word
        // sitting inert beside it, which is worse than both falling back to naming themselves.
        if (marked.Count == 1)
        {
            return marked[0];
        }

        return marked.Count > 1 ? null : NameMatch.Best(available, said, q => q.Name, q => q.Key);
    }

    /// <summary>The quests this mob gives, in authored order. Empty when quests are unavailable.</summary>
    private static IReadOnlyList<Quest> OfferedBy(CommandContext ctx, Mob mob) =>
        ctx.Quests is { IsLoaded: true }
            ? [.. ctx.Quests.GetByGiverMobKey(mob.TemplateKey).OrderBy(q => q.SortOrder)]
            : [];

    /// <summary>Whether this character could take this quest on right now.</summary>
    private static bool Startable(CommandContext ctx, Guid characterId, Quest quest)
    {
        var state = ctx.World.GetQuestState(characterId, quest.Key);

        if (state is null)
        {
            return CheckPrerequisites(ctx.World, characterId, quest);
        }

        return state.Status == QuestStatus.Completed
            && quest.IsRepeatable
            && ChainStillRunning(ctx, characterId, quest.Key) is null
            && UpstreamHasRunAgain(ctx, characterId, quest, state);
    }

    /// <summary>
    /// What the giver says when offering, without starting anything. Markers included — every
    /// caller either renders them (<see cref="Offer"/>) or reads them (<see cref="Match"/>).
    /// </summary>
    private static string OfferText(Quest quest) =>
        quest.Dialogue.TryGetValue(QuestDialogue.GiverOffer, out var offer)
            ? offer
            : $"I have a job for you: {quest.Summary}";

    /// <summary>
    /// What a mob with nothing to offer says: their own line, their counter, or a stock one.
    /// </summary>
    /// <remarks>
    /// Authored greetings are a list and are cycled by pulse rather than chosen at random, because
    /// the command layer has no random source and a clock it already has does the job — the same
    /// line twice running is what makes a world read thin, and which order they arrive in does not
    /// matter.
    /// </remarks>
    private static string SmallTalk(CommandContext ctx, Mob mob)
    {
        var behavior = ctx.MobTemplates?.Get(mob.TemplateKey)?.Behavior;
        var greetings = MobBehavior.GreetingsOf(behavior);

        if (greetings.Count > 0)
        {
            var index = (int)(Math.Abs(ctx.Clock?.CurrentPulse ?? 0) % greetings.Count);
            return greetings[index];
        }

        var name = NarrationHelper.WithDefiniteArticle(mob.DisplayName, capitalize: true);

        // A shopkeeper always has something to say, and `list` is three characters that a player
        // has no way to discover from the room description.
        if (MobBehavior.IsShopkeeper(behavior))
        {
            return $"{name} nods towards the counter. Try 'list' to see what is for sale.";
        }

        return MobBehavior.DispositionOf(behavior) == MobDisposition.Aggressive
            ? $"{name} is in no mood for talking."
            : $"{name} has nothing to say just now.";
    }

    /// <summary>
    /// Ways of addressing this mob, likeliest first, for <see cref="Command"/> to try in turn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The last word is wrong more often than it is right.</b> That was the rule here, on the
    /// reasoning that English puts the noun last — true of "a bar maiden", and false of every
    /// giver in the Reaches but two. It told players to type <c>talk house</c> at Deacon Pell of
    /// Ilvaro's house, <c>talk gates</c> at Vesh, who follows the gates, and <c>talk expelled</c>
    /// at Sister Aveth, who was expelled. Six of eight givers, including the one who hands out the
    /// first quest in the game.
    /// </para>
    /// <para>
    /// What those names have in common is a trailing clause: the mob is named, and then described.
    /// So the name is cut at the first word that opens one, and the noun is taken from what is
    /// left — "Deacon Pell" gives <c>pell</c>. The whole name and the template key follow as
    /// fallbacks, and <see cref="Command"/> keeps the first that actually reaches this mob, so a
    /// name this heuristic reads badly costs nothing but a longer command.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Addresses(Mob mob)
    {
        var words = Words(mob.TemplateName);
        var core = new List<string>();

        foreach (var word in words)
        {
            if (core.Count > 0 && Clausal.Contains(word))
            {
                break;
            }

            core.Add(word);
        }

        if (core.Count > 0)
        {
            yield return core[^1];
        }

        if (words.Count > core.Count)
        {
            yield return string.Join(' ', core);
        }

        if (!string.IsNullOrWhiteSpace(mob.TemplateName))
        {
            yield return mob.TemplateName;
        }

        yield return mob.TemplateKey;
    }

    /// <summary>
    /// Words that begin a describing clause, and so end the part of a name that names somebody.
    /// </summary>
    private static readonly HashSet<string> Clausal = new(StringComparer.OrdinalIgnoreCase)
    {
        "at", "by", "for", "from", "in", "of", "that", "the", "to", "which", "who", "whose",
        "with",
    };

    /// <summary>
    /// A word from the quest's name to suggest saying, for the stage-direction line.
    /// </summary>
    /// <remarks>
    /// <b>Display only, and deliberately not load-bearing.</b> Matching accepts any word of the
    /// name or key, so a poor suggestion costs nothing — which is what lets this be a heuristic at
    /// all. It takes the longest word that is not a grammatical one, because the last word is
    /// often the weakest: "The Road Out" would suggest <c>out</c>, and "What It Is Watching For"
    /// would suggest <c>for</c>.
    /// </remarks>
    private static string Cue(Quest quest)
    {
        var words = Words(quest.Name)
            .Where(w => !Grammatical.Contains(w))
            .OrderByDescending(w => w.Length)
            .ToList();


        return (words.Count > 0 ? words[0] : quest.Key).ToLowerInvariant();
    }

    /// <summary>Words that carry no meaning on their own, for <see cref="Cue"/>.</summary>
    private static readonly HashSet<string> Grammatical = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "at", "for", "in", "is", "it", "not", "of", "on", "or", "out",
        "the", "to", "was", "what", "who", "with",
    };

    /// <summary>What was said, whole first and then word by word, longest word first.</summary>
    /// <remarks>
    /// Longest first so the most specific word gets its chance before a short one that happens to
    /// collide — "I will find your ledger" should reach the ledger quest, not something called
    /// "I".
    /// </remarks>
    private static IEnumerable<string> Spoken(string said) =>
        [said, .. Words(said).OrderByDescending(w => w.Length)];

    /// <summary>
    /// Splits authored text into its words, <b>in the order they were written</b>.
    /// </summary>
    /// <remarks>
    /// Order is load-bearing for <see cref="Address"/>, which wants the last word because that is
    /// the noun. Callers that want the most distinctive word sort for themselves — this returning
    /// them pre-sorted is what made "the village elder" answer to "the".
    /// </remarks>
    private static List<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split([' ', '-', '_', ',', '.', '\'', '"'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Whether this quest points at content that no longer exists.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, and deliberately not a <c>QuestStatus.Unavailable</c>. The
    /// state is entirely a function of what content exists right now, so deriving it means a
    /// builder who deletes a mob by mistake and puts it back has every stranded quest working
    /// again with no repair pass - and a stored status would need one, plus a migration, plus a
    /// rule for when to flip it back.
    ///
    /// What §7.4 actually requires is that <c>character_quests</c> rows survive and that the
    /// giver stops offering it. Both are true here, and no player row is ever written.
    ///
    /// Keys are not foreign keys on purpose (§7.4: a builder wires a quest before creating what
    /// it references), so "missing" is normal and temporary rather than corrupt.
    /// </remarks>
    private static bool IsDormant(CommandContext ctx, Quest quest)
    {
        if (ctx.MobTemplates is null)
        {
            return false;
        }

        if (ctx.MobTemplates.Get(quest.GiverMobKey) is null ||
            ctx.MobTemplates.Get(quest.TurninMobKey) is null)
        {
            return true;
        }

        // The required item only makes a quest dormant if it is named at all - a quest with no
        // fetch step is legitimate, which is why RequiredItemKey is nullable.
        return !string.IsNullOrEmpty(quest.RequiredItemKey) &&
               ctx.ItemTemplates?.Get(quest.RequiredItemKey) is null;
    }

    /// <summary>
    /// Whether this quest is meant for this character's Path.
    /// </summary>
    /// <remarks>
    /// <b>Empty means anyone</b>, exactly as <c>ItemTemplate.Paths</c> does, so the fifteen quests
    /// authored before this field are unrestricted without being touched.
    ///
    /// The four epic chains are why this exists. All twenty are given by one smith, so Vesh handed
    /// every character all four - and a Temper who finished the Adept chain received a stormrod they
    /// could not wield and, being lore and no-drop, could not get rid of either.
    /// </remarks>
    private static bool IsForPath(Quest quest, Character character) =>
        quest.Paths.Count == 0 || quest.Paths.Contains(character.Path);

    /// <summary>
    /// What to say to somebody holding a quest their Path cannot finish, or null when there is
    /// nothing to say.
    /// </summary>
    /// <remarks>
    /// Held, not removed. A gate edited by mistake would otherwise wipe journals silently, and
    /// taking progress away from under a player is worse than telling them plainly - <c>abandon</c>
    /// is how they clear it, and it already exists.
    /// </remarks>
    private static string? WrongPathNote(Quest quest, Character character) =>
        IsForPath(quest, character)
            ? null
            : $"{quest.Name} was never yours to finish — it is not {character.Path} work. "
              + $"'abandon {quest.Name.ToLowerInvariant()}' when you are ready to let it go.";

    private static void Quests(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var questList = ctx.World.QuestsFor(character.Id);

        if (questList.Count == 0)
        {
            ctx.Reply("You have no quests.");
            return;
        }

        var active = questList.Where(q => q.Status == QuestStatus.Active).ToList();
        var completed = questList.Where(q => q.Status == QuestStatus.Completed).ToList();

        ctx.Reply("=== Your Quests ===");

        if (active.Count > 0)
        {
            ctx.Reply("Active:");
            foreach (var quest in active)
            {
                var questDef = ctx.Quests?.Get(quest.QuestKey);
                var summary = questDef?.Summary ?? "Unknown quest";

                // A dormant quest is still yours and still in the journal - it just cannot be
                // progressed until the content comes back. Saying so beats leaving a player
                // hunting for an item that no longer exists (PLAN.md §7.4).
                var dormant = questDef is not null && IsDormant(ctx, questDef);
                var suffix = dormant ? " (unavailable — content missing)" : string.Empty;

                // The count and the item's own name beside the summary, so the journal answers
                // "what am I carrying this for" without a second command. The summary is prose
                // and may call the thing what the giver calls it; this is the name in the pack.
                var progress = !dormant && !string.IsNullOrEmpty(questDef?.RequiredItemKey)
                    ? $" ({Carried(ctx, character.Id, questDef.RequiredItemKey)}/{questDef.RequiredCount} "
                        + $"{ItemName(ctx, questDef.RequiredItemKey)})"
                    : string.Empty;

                ctx.Reply($"  {questDef?.Name ?? quest.QuestKey}: {summary}{progress}{suffix}");
            }
        }

        if (completed.Count > 0)
        {
            ctx.Reply("Completed:");
            foreach (var quest in completed)
            {
                var questDef = ctx.Quests?.Get(quest.QuestKey);
                ctx.Reply($"  {questDef?.Name ?? quest.QuestKey}");
            }
        }
    }

    /// <summary>
    /// Gives up an active quest, returning it to never-started so the giver will offer it again.
    /// </summary>
    /// <remarks>
    /// A chain is the reason this exists. Prerequisites mean an abandoned leg blocks every quest
    /// behind it, and before this there was no way out of one: the journal listed it Active for
    /// ever and the giver answered with its in-progress line. That is a soft-lock made of
    /// dialogue rather than of code, which is the hardest kind to notice.
    ///
    /// It removes the state rather than marking it, because §6 spells "not started" as the
    /// absence of a row - so no new status, no migration, and nothing else has to learn a third
    /// state. The one exception is a repeatable quest already finished at least once: deleting
    /// that row would erase the history in <c>TimesCompleted</c>, so it reverts to Completed and
    /// keeps the count.
    ///
    /// Items are deliberately left alone. Taking them back would be destroying player property on
    /// a verb typed by mistake, and worse, the item may have come from an earlier leg that is no
    /// longer repeatable - which would make the chain permanently unfinishable rather than merely
    /// abandoned. A held item is not dead weight either: <c>drop</c> carries no quest-item guard,
    /// only <c>destroy</c> and <c>sell</c> do, so it can be put down and picked back up if the
    /// quest is taken again.
    /// </remarks>
    private static void Abandon(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Abandon which quest?");
            return;
        }

        var character = ctx.Actor.Character;

        var questState = FindQuestByName(ctx, character.Id, ctx.Argument);

        if (questState is null)
        {
            ctx.Reply("You don't have that quest.");
            return;
        }

        var name = ctx.Quests?.Get(questState.QuestKey)?.Name ?? questState.QuestKey;

        if (questState.Status != QuestStatus.Active)
        {
            // Named rather than generic: "you don't have that quest" would be a lie about
            // something sitting in the journal two lines above.
            ctx.Reply($"You have already finished {name}.");
            return;
        }

        if (questState.TimesCompleted > 0)
        {
            questState.Status = QuestStatus.Completed;
            ctx.World.SetQuestState(character.Id, questState.QuestKey, questState);
            ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                character.Id,
                questState.QuestKey,
                QuestStatus.Completed,
                questState.StartedAt,
                questState.CompletedAt,
                questState.TimesCompleted));
        }
        else
        {
            ctx.World.RemoveQuestState(character.Id, questState.QuestKey);
            ctx.QuestSaveQueue?.EnqueueDelete(character.Id, questState.QuestKey);
        }

        ctx.Reply($"You give up on {name}. You can ask for it again.");
    }

    /// <summary>
    /// Puts a quest in a character's journal as Active. <b>Says nothing</b> — the two ways in
    /// want different lines.
    /// </summary>
    /// <remarks>
    /// Answering a giver gets <see cref="Instruction"/>, because the offer has just been read.
    /// A chain step that starts itself gets the offer, because nobody pitched it — and it is the
    /// plain offer, since a link to take on a quest already in the journal would be a lie.
    /// </remarks>
    /// <remarks>
    /// One place, because there are now three ways in — a first offer, a repeat, and a chain step
    /// starting itself — and three copies of "set Active, keep the count, enqueue the snapshot"
    /// is three chances to forget the count. <paramref name="timesCompleted"/> is carried rather
    /// than reset because it is the history the repeat gates read.
    /// </remarks>
    private static void Begin(
        CommandContext ctx, Guid characterId, Quest quest, int timesCompleted)
    {
        var startedAt = DateTimeOffset.UtcNow;

        ctx.World.SetQuestState(characterId, quest.Key, new CharacterQuest
        {
            CharacterId = characterId,
            QuestKey = quest.Key,
            Status = QuestStatus.Active,
            StartedAt = startedAt,
            TimesCompleted = timesCompleted,
        });

        ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
            characterId, quest.Key, QuestStatus.Active, startedAt, null, timesCompleted));

    }

    /// <summary>What a quest wants brought, by the name the player will see in their pack.</summary>
    /// <remarks>
    /// Said when the quest is taken on, because the offer and the instruction are the giver talking,
    /// and a giver may call a thing whatever they like. Playtesting found offers that never named
    /// the item at all, so a player could take on a job without being told what to fetch.
    /// </remarks>
    private static void ReplyNeeds(CommandContext ctx, Quest quest)
    {
        if (!string.IsNullOrEmpty(quest.RequiredItemKey))
        {
            ctx.Reply($"You will need {Quantity(ctx, quest.RequiredItemKey, quest.RequiredCount)}.");
        }
    }

    /// <summary>How many of an item this character is carrying.</summary>
    private static int Carried(CommandContext ctx, Guid characterId, string itemKey) =>
        ctx.World.InventoryOf(characterId)
            .Count(i => i.TemplateKey.Equals(itemKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The item a <c>give</c> to a quest's turn-in means, when more than one thing carried answers
    /// to the word. Null when the target takes in nothing this character is fetching.
    /// </summary>
    /// <remarks>
    /// <b>The quest wins a tie.</b> A vigil token, a debt token and a gate-shrine token all answer
    /// to "token", and the pack's order used to decide which one <c>give token aveth</c> meant - so
    /// a player holding the right item could hand over the wrong one as a gift, to someone who had
    /// just asked for the right one. Only the items an active quest of this turn-in wants are
    /// considered, so this never changes which item a give to anybody else picks.
    /// </remarks>
    public static ItemInstance? TurnInItemFor(
        CommandContext ctx, IEnumerable<ItemInstance> inventory, string itemName, string npcName)
    {
        if (ctx.Quests is not { IsLoaded: true })
        {
            return null;
        }

        var character = ctx.Actor.Character;
        var mob = NameMatch.Best(
            ctx.World.MobsIn(character.RoomKey), npcName, m => m.TemplateName, m => m.TemplateKey);

        if (mob is null)
        {
            return null;
        }

        var wanted = ctx.Quests.GetByTurninMobKey(mob.TemplateKey)
            .Where(q => !string.IsNullOrEmpty(q.RequiredItemKey)
                && ctx.World.GetQuestState(character.Id, q.Key)?.Status == QuestStatus.Active)
            .Select(q => q.RequiredItemKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return wanted.Count == 0
            ? null
            : NameMatch.Best(
                inventory.Where(i => wanted.Contains(i.TemplateKey)),
                itemName,
                i => i.TemplateName,
                i => i.TemplateKey);
    }

    /// <summary>What to actually do: the same line the giver repeats when asked again later.</summary>
    private static string Instruction(Quest quest) =>
        quest.Dialogue.TryGetValue(QuestDialogue.GiverInProgress, out var inProgress)
            ? inProgress
            : $"Right. {quest.Summary}";

    /// <summary>
    /// Starts any quest that follows the one just finished and has asked to begin by itself.
    /// </summary>
    /// <remarks>
    /// Driven off <see cref="Quest.PrerequisiteQuestKeys"/> rather than a list of triggers on the
    /// quest that completed, so there is still exactly one description of what follows what — the
    /// one the storyline panel already draws. A second set of edges would be invisible there, and
    /// nothing would keep the two in agreement.
    ///
    /// Called after the turn-in has marked its own quest Completed, which is what lets the
    /// ordinary prerequisite check do the work here: a step whose other prerequisites are still
    /// open simply does not qualify yet. Every rule a <c>talk</c> would apply is applied — the
    /// dormancy check, both repeat gates — because a quest that starts itself must not be able to
    /// reach a state a player could not have reached by asking for it.
    ///
    /// It does not cascade: the quest it starts is Active, not Completed, so nothing downstream of
    /// <em>that</em> qualifies until it is finished in turn.
    /// </remarks>
    private static void StartFollowOnQuests(CommandContext ctx, Guid characterId, string completedKey)
    {
        if (ctx.Quests is null)
        {
            return;
        }

        foreach (var next in ctx.Quests.All.Values
            .Where(q => q.AutoStart
                && q.PrerequisiteQuestKeys.Contains(completedKey, StringComparer.OrdinalIgnoreCase))
            .OrderBy(q => q.SortOrder))
        {
            if (IsDormant(ctx, next) || !CheckPrerequisites(ctx.World, characterId, next))
            {
                continue;
            }

            // A chain that starts itself must not reach a state a player could not have reached by
            // asking for it - the same rule this method already applies to dormancy and repeats.
            if (ctx.World.GetCharacter(characterId) is { } follower && !IsForPath(next, follower))
            {
                continue;
            }

            var state = ctx.World.GetQuestState(characterId, next.Key);

            if (state is null)
            {
                Begin(ctx, characterId, next, timesCompleted: 0);
                ctx.Reply(QuestOffer.Plain(OfferText(next)));
                ReplyNeeds(ctx, next);
                continue;
            }

            // Already in the journal. Only a finished, repeatable one may start again, and only
            // under the same two gates a talk would apply.
            if (state.Status != QuestStatus.Completed
                || !next.IsRepeatable
                || ChainStillRunning(ctx, characterId, next.Key) is not null
                || !UpstreamHasRunAgain(ctx, characterId, next, state))
            {
                continue;
            }

            Begin(ctx, characterId, next, state.TimesCompleted);
            ctx.Reply(QuestOffer.Plain(OfferText(next)));
            ReplyNeeds(ctx, next);
        }
    }

    /// <summary>
    /// Whether every prerequisite has been finished more times than this quest has, which is what
    /// makes a repeat a fresh run through the chain rather than a re-entry into the middle of it.
    /// </summary>
    /// <remarks>
    /// <c>TimesCompleted</c> already counts the runs, so the comparison needs no new state: after
    /// one full pass both legs sit at 1 and the second is not offered again; run the first leg
    /// once more and it is at 2, which is what unlocks the second.
    ///
    /// A quest with no prerequisites is the head of its chain and always passes — otherwise
    /// nothing would ever be repeatable at all.
    /// </remarks>
    private static bool UpstreamHasRunAgain(
        CommandContext ctx, Guid characterId, Quest quest, CharacterQuest state)
    {
        foreach (var prerequisiteKey in quest.PrerequisiteQuestKeys)
        {
            var prerequisite = ctx.World.GetQuestState(characterId, prerequisiteKey);

            if (prerequisite is null || prerequisite.TimesCompleted <= state.TimesCompleted)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The quest still open somewhere downstream of <paramref name="questKey"/>, or null if the
    /// chain below it is clear.
    /// </summary>
    /// <remarks>
    /// <b>A repeatable quest is repeatable once the chain it starts is done, not once its own leg
    /// is.</b> Repeatability was a property of the single quest, so a player who had finished
    /// <c>A Fresh Drink</c> and was carrying the glass could take the errand again mid-chain: the
    /// first leg reset to Active while the second stayed Active behind it, and the journal then
    /// described a state the story cannot be in — the beer not yet delivered, the glass already
    /// in hand.
    ///
    /// Downstream is transitive, because a chain is not only two long. It is walked rather than
    /// stored, for the reason dormancy is derived (§7.4): prerequisites are edited live, and a
    /// cached answer would be wrong the moment a builder inserted a step.
    ///
    /// Only <em>Active</em> blocks. A completed step downstream is exactly the case this is meant
    /// to allow, and the visited set is what keeps an authored cycle — which the storyline panel
    /// reports but does not prevent — from walking forever.
    /// </remarks>
    private static Quest? ChainStillRunning(CommandContext ctx, Guid characterId, string questKey)
    {
        if (ctx.Quests is null)
        {
            return null;
        }

        var all = ctx.Quests.All;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { questKey };
        var frontier = new Queue<string>();
        frontier.Enqueue(questKey);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var candidate in all.Values)
            {
                if (!candidate.PrerequisiteQuestKeys.Contains(current, StringComparer.OrdinalIgnoreCase)
                    || !visited.Add(candidate.Key))
                {
                    continue;
                }

                if (ctx.World.GetQuestState(characterId, candidate.Key)?.Status == QuestStatus.Active)
                {
                    return candidate;
                }

                frontier.Enqueue(candidate.Key);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds one of a character's quests by a fragment of its display name.
    /// </summary>
    /// <remarks>
    /// Shared by <c>quest</c> and <c>abandon</c> so the two agree about what a player means -
    /// two copies of a fuzzy match is two chances to disagree about which quest was named.
    /// </remarks>
    private static CharacterQuest? FindQuestByName(CommandContext ctx, Guid characterId, string name) =>
        ctx.World.QuestsFor(characterId).FirstOrDefault(q =>
            ctx.Quests?.Get(q.QuestKey)?.Name.Contains(name, StringComparison.OrdinalIgnoreCase) == true);

    private static void QuestDetail(CommandContext ctx)
    {
        // Bare `quest` shows the journal rather than asking "Which quest?". The two verbs are one
        // family and the argument is what distinguishes them, so a player who types the shorter
        // word gets the more useful answer instead of a question. It also means every
        // abbreviation from "que" up stays useful now that this definition is matched first.
        if (!ctx.HasArgument)
        {
            Quests(ctx);
            return;
        }

        var character = ctx.Actor.Character;
        var questState = FindQuestByName(ctx, character.Id, ctx.Argument);

        if (questState is null)
        {
            ctx.Reply("You don't have that quest.");
            return;
        }

        var questDef = ctx.Quests?.Get(questState.QuestKey);
        if (questDef is null)
        {
            ctx.Reply("Quest information is unavailable.");
            return;
        }

        ctx.Reply($"=== {questDef.Name} ===");
        if (!string.IsNullOrEmpty(questDef.Description))
        {
            ctx.Reply(questDef.Description);
        }

        if (!string.IsNullOrEmpty(questDef.Summary))
        {
            ctx.Reply($"Objective: {questDef.Summary}");
        }

        // Show progress, but only for a quest that has something to count. RequiredItemKey is
        // nullable by design, and with it null nothing ever matches - so a non-fetch quest printed
        // `Progress: 0/0 — something`, a bar that could not move and a noun that named nothing
        // (BUGS.md #16).
        if (questState.Status == QuestStatus.Active && !string.IsNullOrEmpty(questDef.RequiredItemKey))
        {
            var count = Carried(ctx, character.Id, questDef.RequiredItemKey);
            ctx.Reply($"Progress: {count}/{questDef.RequiredCount} — {ItemName(ctx, questDef.RequiredItemKey)}");
        }
        else if (questState.Status == QuestStatus.Active)
        {
            ctx.Reply("Status: In progress");
        }
        else if (questState.Status == QuestStatus.Completed)
        {
            ctx.Reply("Status: Completed");
        }

        // Show rewards
        ctx.Reply("Rewards:");
        if (questDef.RewardXp > 0)
        {
            ctx.Reply($"  {questDef.RewardXp} experience");
        }

        if (questDef.RewardGold > 0)
        {
            ctx.Reply($"  {questDef.RewardGold} gold");
        }

        if (!string.IsNullOrEmpty(questDef.RewardItemKey))
        {
            ctx.Reply($"  {Quantity(ctx, questDef.RewardItemKey, questDef.RewardItemCount)}");
        }
    }

    /// <summary>
    /// What to call a quest's item on screen, from the template rather than from an instance.
    /// </summary>
    /// <remarks>
    /// <b>A quest names its item by key, and there is no instance here to ask.</b> That is why the
    /// three lines below leaked <c>ossara-fallen-marker</c> at players while every mob and item
    /// instance in the game had already been fixed: <see cref="Domain.Items.ItemInstance.DisplayName"/>
    /// answers for a thing that exists, and a quest requirement is a thing that might not — a
    /// player who holds none of them still has to be told what to go and find.
    ///
    /// Falls back to the key for the same reason everything else does: a missing template is a
    /// content bug, and the key is the only string left that tells the player anything at all.
    /// </remarks>
    private static string ItemName(CommandContext ctx, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "something";
        }

        var name = ctx.ItemTemplates?.Get(key)?.Name;
        return string.IsNullOrEmpty(name) ? key : name;
    }

    /// <summary>
    /// A quantity of one item, in the <c>(xN)</c> shape the pack listing already uses (§4.14).
    /// </summary>
    /// <remarks>
    /// No pluralisation. Item names carry their own article — "a fallen road marker" — so "3 a
    /// fallen road markers" is what naive pluralising produces, and the codebase already settled
    /// this question once for the inventory.
    /// </remarks>
    private static string Quantity(CommandContext ctx, string? key, int count) =>
        count > 1 ? $"{ItemName(ctx, key)} (x{count})" : ItemName(ctx, key);

    private static bool CheckPrerequisites(WorldState world, Guid characterId, Quest quest)
    {
        if (quest.PrerequisiteQuestKeys.Count == 0)
        {
            return true;
        }

        foreach (var prereqKey in quest.PrerequisiteQuestKeys)
        {
            var prereqState = world.GetQuestState(characterId, prereqKey);
            if (prereqState?.Status != QuestStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to handle a quest turn-in. Returns true if a quest was turned in, false if not.
    /// Called from the Give command to check for NPC quest turn-ins before player-to-player gives.
    /// </summary>
    public static bool TryTurnInQuest(CommandContext ctx, string itemName, string npcName)
    {
        if (ctx.Quests is null || !ctx.Quests.IsLoaded)
        {
            return false;
        }

        var character = ctx.Actor.Character;

        // Find the mob in the current room
        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(character.RoomKey), npcName, m => m.TemplateName, m => m.TemplateKey);

        if (targetMob is null)
        {
            return false;
        }

        // Find quests that can be turned in to this mob
        var turnInQuests = ctx.Quests.GetByTurninMobKey(targetMob.TemplateKey);

        if (turnInQuests.Count == 0)
        {
            return false;
        }

        // Find a quest the character has that matches this item and NPC
        Quest? matchingQuest = null;
        foreach (var quest in turnInQuests)
        {
            var questState = ctx.World.GetQuestState(character.Id, quest.Key);
            if (questState?.Status == QuestStatus.Active &&
                string.Equals(quest.RequiredItemKey, itemName, StringComparison.OrdinalIgnoreCase))
            {
                matchingQuest = quest;
                break;
            }
        }

        if (matchingQuest is null)
        {
            return false;
        }

        // Refused here as well as at the offer, because a character can be holding a quest the
        // gate would not hand out now: one taken before the Paths were authored, or before this
        // rule existed. Handing over the reward anyway is the whole defect - the item is Path
        // locked, lore and no-drop, so it lands in a pack that can never use it or empty it.
        if (WrongPathNote(matchingQuest, character) is { } wrongPath)
        {
            ctx.Reply(wrongPath);

            // True, not false: the give *was* about this quest and was answered. Returning false
            // would fall through to the ordinary give, handing the smith the embers for nothing.
            return true;
        }

        // Count items in inventory that match the quest requirement
        var inventory = ctx.World.InventoryOf(character.Id);
        var matchingItems = inventory
            .Where(i => i.TemplateKey.Equals(matchingQuest.RequiredItemKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingItems.Count < matchingQuest.RequiredCount)
        {
            // The count, not just "not enough": without it the player has to go and tally their
            // own pack to find out how far off they are, which is the one thing this line exists
            // to save them.
            ctx.Reply(
                $"You need {Quantity(ctx, matchingQuest.RequiredItemKey, matchingQuest.RequiredCount)}. "
                + $"You have {matchingItems.Count}.");
            return true;
        }

        // Remove the required items, in storage as well as in the world - otherwise the turn-in
        // is undone by a restart and the quest can be handed in again with the same items.
        for (var i = 0; i < matchingQuest.RequiredCount; i++)
        {
            ctx.World.RemoveItem(matchingItems[i]);
            ctx.ItemSaveQueue?.EnqueueDelete(matchingItems[i].Id);
        }

        // Mark quest as completed
        var completedQuestState = ctx.World.GetQuestState(character.Id, matchingQuest.Key);
        if (completedQuestState is not null)
        {
            completedQuestState.Status = QuestStatus.Completed;
            completedQuestState.CompletedAt = DateTimeOffset.UtcNow;
            completedQuestState.TimesCompleted++;
            ctx.World.SetQuestState(character.Id, matchingQuest.Key, completedQuestState);

            // Persist the state change
            ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                character.Id, matchingQuest.Key, QuestStatus.Completed, completedQuestState.StartedAt,
                DateTimeOffset.UtcNow, completedQuestState.TimesCompleted));
        }

        // Narrate turn-in
        var turninReady = matchingQuest.Dialogue.TryGetValue(QuestDialogue.TurninReady, out var turninText)
            ? turninText
            : $"Excellent work! You've completed {matchingQuest.Name}.";
        ctx.Reply(turninReady);

        AwardRewards(ctx, matchingQuest, character);

        // Handle level up
        var startingLevel = character.Level;

        while (Muwbta.Domain.Characters.CharacterProgression.TryLevelUp(
            character.Level, character.Xp, character.Attributes, character.Path, character.Vitals) is var result && result != null)
        {
            character.Level = result.NewLevel;
            character.Attributes = result.NewAttributes;
            character.Vitals = result.NewVitals;
            ctx.Reply($"You advance to level {result.NewLevel}!", "levelup");
        }

        // The same announcement combat makes, for the same reason: a quest that pays a whole band
        // of levels at once grants abilities the player is never otherwise told about.
        Presentation.PlayerView.SendUnlocks(ctx.Actor, ctx.Abilities, startingLevel);

        // Last, so the next step's offer reads as the consequence of the turn-in rather than
        // arriving in the middle of its rewards. By this point the quest is Completed, which is
        // what lets the ordinary prerequisite check decide whether anything follows.
        StartFollowOnQuests(ctx, character.Id, matchingQuest.Key);

        return true;
    }

    /// <summary>
    /// Pays out a completed quest: XP, gold, and reward items, all through the zone's
    /// difficulty dial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rewards used to be paid raw while combat awarded <c>mob.ResolvedXp</c>, so the same zone
    /// multipliers that made a mob worth triple left the quest beside it worth exactly its
    /// authored number. §7.5 calls multipliers "the reason the whole feature exists"; a reward
    /// path that ignores them makes quests the one thing the dial does not move.
    /// </para>
    /// <para>
    /// <b>And then through the same relevance window a kill goes through</b>
    /// (<see cref="XpRelevance"/>, §4.7). Half of that rule was in place and half was not: a level
    /// 50 killing a level 28 mob earns a fraction, and the same level 50 turning in that zone's
    /// quest was earning all of it. A whole chain of them was the best experience in the game for
    /// somebody with no business being there, which is precisely what the window exists to stop.
    /// </para>
    /// <para>
    /// <b>Gold is not scaled by it</b>, matching kills (§4.7): experience is credit for the fight
    /// and gold is payment for being there. Only the experience asks whether the fight was worth
    /// having.
    /// </para>
    /// </remarks>
    private static void AwardRewards(CommandContext ctx, Quest quest, Character character)
    {
        var zone = ctx.World.FindZone(quest.ZoneKey);
        var world = zone is not null ? ctx.World.FindWorld(zone.WorldKey) : null;

        // A quest whose zone or world is missing is a content bug, not a reason to pay nothing.
        // Falling back to the authored numbers keeps the player whole while the builder fixes it;
        // silently awarding zero is what this did before, and it looked like a balance decision.
        var worldMultipliers = world?.Multipliers;
        var zoneMultipliers = zone?.Multipliers;

        // The window after the multipliers, in that order, for the reason XpRelevance gives: a
        // zone may scale a reward and must never be able to resurrect a worthless one.
        var xp = (int)XpRelevance.ShareOf(
            Resolve(quest.RewardXp, MultiplierType.Xp), character.Level, LevelOf(zone, character));
        var gold = Resolve(quest.RewardGold, MultiplierType.Gold);

        character.Xp += xp;
        character.Gold += gold;

        if (xp > 0)
        {
            ctx.Reply($"You gain {xp} experience points.", "reward");
        }

        if (gold > 0)
        {
            ctx.Reply($"You gain {gold} gold.", "reward");
        }

        AwardRewardFlag(ctx, quest, character);
        AwardRewardItems(ctx, quest, character, zone, world);
        return;

        int Resolve(int amount, MultiplierType type) =>
            worldMultipliers is null || zoneMultipliers is null
                ? amount
                : Multipliers.Resolve(amount, worldMultipliers, zoneMultipliers, type);
    }

    /// <summary>
    /// What level a quest counts as, for the relevance window.
    /// </summary>
    /// <remarks>
    /// <b>The top of the zone's band, not the bottom.</b> A quest belongs to the whole range its
    /// author declared, so anybody still inside that range should be paid in full — measuring
    /// against <c>MinLevel</c> would dock a player for having finished the zone the quest is in,
    /// which is the opposite of the intent. Only somebody who has out-levelled the entire band
    /// sees the taper.
    ///
    /// Falls back to the character's own level when the zone is missing, which yields full value:
    /// a content bug should not quietly cost a player their reward, and the <c>Zone.MaxLevel</c>
    /// default of 50 fails in the same direction for a zone whose band was never set.
    /// </remarks>
    private static int LevelOf(Zone? zone, Character character) =>
        zone is null ? character.Level : Math.Max(zone.MinLevel, zone.MaxLevel);

    /// <summary>
    /// Grants the capability this quest opens, if it opens one (PLAN.md §4.15) — the only thing in
    /// the game that writes <see cref="Character.Flags"/>.
    /// </summary>
    /// <remarks>
    /// <b>Granted, never toggled, and idempotent.</b> A repeatable quest completed twice must not
    /// hold the flag twice, and finishing a quest again can never take a capability away — a
    /// player who re-ran the chain that attuned them to a Reach and came out unable to go there
    /// would have no way to understand what had happened.
    ///
    /// Unlike the reward item, a flag has nothing that can be missing: it is a string, not a
    /// lookup, so there is no content-bug case to narrate. Whether anything grants a flag an exit
    /// asks for is a builder-side question, answered by <c>/validate</c> rather than here.
    /// </remarks>
    private static void AwardRewardFlag(CommandContext ctx, Quest quest, Character character)
    {
        if (string.IsNullOrEmpty(quest.RewardFlagKey) || character.HasFlag(quest.RewardFlagKey))
        {
            return;
        }

        character.Flags.Add(quest.RewardFlagKey);
        ctx.Reply("Something that was closed to you is not any more.", "reward");
    }

    /// <summary>Hands over the quest's reward item, if it has one.</summary>
    private static void AwardRewardItems(
        CommandContext ctx,
        Quest quest,
        Character character,
        Zone? zone,
        Muwbta.Domain.Worlds.World? world)
    {
        if (string.IsNullOrEmpty(quest.RewardItemKey) || quest.RewardItemCount <= 0)
        {
            return;
        }

        // Each failure below says so rather than returning quietly. A reward that does not
        // arrive and does not explain itself reads to a player as the quest being broken, and
        // to a builder as nothing at all.
        var itemTemplate = ctx.ItemTemplates?.Get(quest.RewardItemKey);
        if (itemTemplate is null)
        {
            ctx.Reply(
                $"({quest.RewardItemKey} was promised, but no such item exists any more. Tell a builder.)",
                "bad");
            return;
        }

        if (zone is null || world is null)
        {
            ctx.Reply(
                $"({itemTemplate.Name} was promised, but its zone is missing. Tell a builder.)",
                "bad");
            return;
        }

        // A lore reward is not handed over twice. This is the path an epic chain arrives by, so
        // leaving it out would mean the one flag written for epics was enforced everywhere except
        // where epics come from - and a repeatable chain would mint a second copy on every run.
        if (itemTemplate.IsLore &&
            ItemRules.AlreadyHolds(ctx.World, character.Id, quest.RewardItemKey!))
        {
            ctx.Reply(
                $"(You already carry {itemTemplate.Name}. One is all anyone gets.)",
                "bad");
            return;
        }

        var spawner = new Muwbta.Engine.Spawning.ItemSpawner();

        // Spawned per copy, not once and cloned: each instance needs its own id, and the spawner
        // is the only place that stamps the questItem flag - which quest rewards are the most
        // likely items in the game to carry.
        for (var i = 0; i < quest.RewardItemCount; i++)
        {
            var instance = spawner.Spawn(itemTemplate, zone, world, character.RoomKey);

            ctx.World.AddItem(instance);
            ctx.World.PickUpItem(instance, character.Id);
            ctx.ItemSaveQueue?.Enqueue(instance);
        }

        // Once, after the loop. This sat inside it, so a count of three announced three times
        // that the player had received three.
        var received = quest.RewardItemCount == 1
            ? NarrationHelper.WithArticle(itemTemplate.Name)
            : $"{quest.RewardItemCount} x {itemTemplate.Name}";

        ctx.Reply($"You receive {received}.", "reward");
    }
}
