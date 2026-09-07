using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;

namespace Muwbta.Engine.Commands;

/// <summary>
/// <c>sleep</c>, <c>rest</c> and <c>stand</c> — how hard you are recovering, and how little else
/// you can do while you do it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sleep is geography, and <c>peaceful</c> is the geography it reads.</b> Deep sleep is the best
/// recovery in the game and it costs nothing but the verbs it takes away — which made lying down in
/// a corridor with a wandering mob in it the correct play, since the mob could not reach you before
/// the regen did. Rather than invent a second flag meaning "safe" beside a flag that already means
/// it, this asks the one the world is already marked with: a <c>peaceful</c> room is exactly a room
/// nothing can open a fight in (§4.10), so it is exactly the room where sleeping through the next
/// ten minutes is a decision rather than a gamble. <c>rest</c> stays available everywhere, at half
/// the rate, which is what makes the two verbs different from each other.
/// </para>
/// <para>
/// <b>Inherited, like every flag.</b> A settlement zone or a whole safe world declares it once and
/// every inn, hall and doorway inside it accepts sleepers; nothing needs flagging room by room. It
/// also means <em>content decides where the beds are</em>, and a world that has marked nothing
/// peaceful has no beds at all — that is the flag doing its job, not the gate misfiring.
/// </para>
/// <para>
/// <b>Lying down is where hunger and thirst are worth repeating.</b> <c>NeedsSystem</c> says each
/// one once, on the tick it crosses a threshold, and then never again — so the reminder has to hang
/// off something the player does, and this is the verb where it matters: a need left unanswered
/// slows recovery (<c>Needs.RegenShare</c>), which is the whole reason for lying down in the first
/// place. Settling in to heal without being told the healing is throttled is the version of this
/// that reads as a bug.
/// </para>
/// </remarks>
public static class RestCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "sleep", 1, "sleep (sl) - rest deeply, where it is safe enough to", Sleep));

        commands.Add(new CommandDefinition(
            "rest", 1, "rest (r) - sit and recover", Rest));

        commands.Add(new CommandDefinition(
            "stand", 2, "stand (st) - wake up and be ready", Stand));
    }

    private static void Sleep(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Sleep)
        {
            ctx.Reply("You are already asleep.");
            return;
        }

        // Asked after the already-asleep line, so somebody whose room stopped being peaceful under
        // them is told what they are rather than told off. Phrased as safety rather than as a flag:
        // "this room is not peaceful" is builder vocabulary, and the player's question is whether
        // they can shut their eyes here.
        if (!ctx.World.IsFlagSet(ctx.Actor.Character.RoomKey, RoomFlags.Peaceful))
        {
            ctx.Reply("It is not safe to sleep here. Try 'rest' instead.", "bad");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Sleep;
        ctx.Reply("You lie down and fall into a deep sleep.");
        ctx.Broadcast($"{ctx.Actor.Name} lies down and falls asleep.");
        RemindOfNeeds(ctx);
    }

    private static void Rest(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Rest)
        {
            ctx.Reply("You are already resting.");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Rest;
        ctx.Reply("You sit down to rest and recover.");
        ctx.Broadcast($"{ctx.Actor.Name} sits down to rest.");
        RemindOfNeeds(ctx);
    }

    private static void Stand(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Stand)
        {
            ctx.Reply("You are already standing.");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Stand;
        ctx.Reply("You stand up, ready for action.");
        ctx.Broadcast($"{ctx.Actor.Name} stands up.");
    }

    /// <summary>
    /// Names whatever the character has let slide, and what it is costing them, on the way down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Said to the player only, and only when there is something to say.</b> Nobody else in the
    /// room needs to hear about somebody's belly, and a line on every <c>rest</c> would train the
    /// player to read past this one — the same reason <c>NeedsSystem</c> speaks on the crossing
    /// rather than on the tick.
    /// </para>
    /// <para>
    /// <b>Not on <c>stand</c>.</b> Standing up is leaving, and a reminder is only useful where it
    /// names something to do about it: down here the answer is to eat before settling in.
    /// </para>
    /// </remarks>
    private static void RemindOfNeeds(CommandContext ctx)
    {
        var vitals = ctx.Actor.Character.Vitals;

        if (Needs.Describe(vitals.Hunger, vitals.Thirst) is not { } condition)
        {
            return;
        }

        ctx.Reply($"You are {condition}.", "bad");
        ctx.Reply(
            $"You will recover at {Needs.RegenShare(vitals.Hunger, vitals.Thirst):P0} of your "
            + "normal rate until you see to it.",
            "dim");
    }
}
