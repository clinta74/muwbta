using Muwbta.Domain.Characters;

namespace Muwbta.Engine.Commands;

/// <summary>
/// <c>sleep</c>, <c>rest</c> and <c>stand</c> — how hard you are recovering, and how little else
/// you can do while you do it.
/// </summary>
/// <remarks>
/// <b>Lying down is where hunger and thirst are worth repeating.</b> <c>NeedsSystem</c> says each
/// one once, on the tick it crosses a threshold, and then never again — so the reminder has to hang
/// off something the player does, and this is the verb where it matters: a need left unanswered
/// slows recovery (<c>Needs.RegenShare</c>), which is the whole reason for lying down in the first
/// place. Settling in to heal without being told the healing is throttled is the version of this
/// that reads as a bug.
/// </remarks>
public static class RestCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "sleep", 1, "sleep (sl) - rest deeply and regenerate quickly", Sleep));

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
