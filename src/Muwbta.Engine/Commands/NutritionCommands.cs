using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Engine.Quests;
using Muwbta.Engine.Time;

namespace Muwbta.Engine.Commands;

/// <summary>
/// <c>eat</c>, <c>drink</c> and <c>quaff</c> — the verbs that consume an item.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both need a minimum length of three, and neither is arbitrary.</b> Directions register first
/// and win the prefix race: <c>e</c> and <c>ea</c> already resolve to <c>east</c>, and <c>d</c> and
/// <c>dr</c> to <c>down</c> and <c>drop</c>. <c>VerbReachabilityTests</c> asserts every verb is
/// reachable at its own abbreviation, and will say so loudly if either number moves.
/// </para>
/// <para>
/// <b>They consume, and until now nothing did.</b> <c>RoomExit.RequiredItemKey</c> says out loud
/// that a keyed exit checks the pack and never takes anything, and that was true of the whole game.
/// So an item that is both a key and a drink can now be swallowed — which is a content question
/// rather than a code one, and the answer is to author the drink separately from the container.
/// </para>
/// <para>
/// <b>A potion is a drink with effects</b> (<see cref="ItemTemplate.UseEffects"/>), run through the
/// executors abilities use - a healing draught is <c>heal.restore</c> in a bottle. Every one shares a
/// single timer, and a stunned character cannot drink one. Allowed in a fight, because that is where
/// a draught earns its price; the timer is what stops a pack of them making a fight with no end.
/// </para>
/// </remarks>
public static class NutritionCommands
{
    /// <summary>The executors a consumable's effects run through - the ones abilities use.</summary>
    private static readonly EffectRegistry Effects = new();

    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        commands.Add(new CommandDefinition(
            "eat", 3, "eat <item> - eat something, if it is food", Eat));

        commands.Add(new CommandDefinition(
            "drink", 3, "drink <item> - drink something, if it is drink", Drink));

        // The word people reach for with a potion in their hand. The same verb underneath.
        commands.Add(new CommandDefinition(
            "quaff", 3, "quaff <item> - drink something down, a draught or a tonic", Drink));
    }

    private static void Eat(CommandContext ctx) => Consume(
        ctx,
        verb: "eat",
        past: "eat",
        refusal: "is not food",
        valueOf: t => t.FoodValue,
        answer: (vitals, amount) => vitals.Hunger = Needs.Reduced(vitals.Hunger, amount),
        alreadyFull: vitals => vitals.Hunger == 0,
        nothingLeft: "You could not manage another bite.");

    private static void Drink(CommandContext ctx) => Consume(
        ctx,
        verb: "drink",
        past: "drink",
        refusal: "is not something you can drink",
        valueOf: t => t.DrinkValue,
        answer: (vitals, amount) => vitals.Thirst = Needs.Reduced(vitals.Thirst, amount),
        alreadyFull: vitals => vitals.Thirst == 0,
        nothingLeft: "You are not thirsty.");

    /// <summary>
    /// The shared half of both verbs: find it, check it, take it, say so.
    /// </summary>
    /// <remarks>
    /// One method because the two differ in four strings and a field. Two near-identical copies is
    /// how one of them quietly stops checking quest bindings a year from now.
    /// </remarks>
    private static void Consume(
        CommandContext ctx,
        string verb,
        string past,
        string refusal,
        Func<ItemTemplate, int?> valueOf,
        Action<Vitals, int> answer,
        Func<Vitals, bool> alreadyFull,
        string nothingLeft)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply($"{char.ToUpperInvariant(verb[0])}{verb[1..]} what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var item = NameMatch.Best(inventory, ctx.Argument, i => i.TemplateName, i => i.TemplateKey);

        if (item is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(item.DisplayName);

        // Read from the template, not the instance: an ItemInstance carries only its TemplateKey,
        // and the nourishment is the template's - so a builder who makes bread more filling makes
        // every loaf already baked more filling too.
        var template = ctx.ItemTemplates?.Get(item.TemplateKey);

        if (template is null || valueOf(template) is not { } value || value <= 0)
        {
            ctx.Reply($"{Capitalise(article)} {refusal}.", "bad");
            return;
        }

        // The same two guards destroy applies, and for the same reasons: something worn is not in
        // your hands, and a quest item eaten is progress that cannot be recovered.
        if (item.EquippedSlot is not null)
        {
            ctx.Reply($"You'll have to remove {article} first.", "bad");
            return;
        }

        if (QuestBinding.RefuseDestroy(
                ctx.Quests, ctx.World, ctx.Actor.CharacterId, item, article) is { } bound)
        {
            ctx.Reply(bound, "bad");
            return;
        }

        var character = ctx.Actor.Character;
        var vitals = character.Vitals;
        var effects = template.UseEffects;
        var now = ctx.Clock?.CurrentPulse ?? 0L;

        // A draught is something you do, so a stun stops it the way it stops a cast. Plain bread is
        // not held to that: nobody is so stunned they cannot chew.
        if (effects.Count > 0)
        {
            if (ctx.World.IsStunned(character.Id, now))
            {
                ctx.Reply("You cannot gather yourself.", "bad");
                return;
            }

            if (StillWaiting(ctx, character.Id, template, now) is { } wait)
            {
                ctx.Reply(wait, "bad");
                return;
            }
        }

        var full = alreadyFull(vitals);

        // Refused rather than wasted. Taking the item and giving nothing back is the shape of a bug
        // even when it is the rule, and the player cannot see the number they are already at. A
        // draught is only wasted when the need is met *and* every bar is already full.
        if (full && (effects.Count == 0 || AtBest(vitals)))
        {
            ctx.Reply(effects.Count > 0 ? "You are already at your best." : nothingLeft, "bad");
            return;
        }

        if (!full)
        {
            answer(vitals, value);
        }

        var before = (vitals.Health, vitals.Focus, vitals.Stamina);

        foreach (var effect in effects)
        {
            if (!string.IsNullOrEmpty(effect.Key) && Effects.Get(effect.Key) is { } executor)
            {
                executor.Apply(character, character, effect.Params ?? [], ctx.World.Random);
            }
        }

        if (effects.Count > 0)
        {
            ctx.World.SetAbilityCooldown(character.Id, ItemTemplate.UseCooldownKey, now);
        }

        // Out of the world and out of storage. Removing it in memory alone hands it back on the
        // next load - the comment destroy carries, for the same reason.
        ctx.World.RemoveItem(item);
        ctx.ItemSaveQueue?.EnqueueDelete(item.Id);

        ctx.Reply($"You {past} {article}.", "good");
        ctx.BroadcastSight($"{ctx.Actor.Name} {past}s {article}.", "movement");

        if (effects.Count > 0)
        {
            ctx.Reply(Restored(before, vitals), "good");
        }

        var remaining = verb == "eat"
            ? Needs.DescribeHunger(vitals.Hunger)
            : Needs.DescribeThirst(vitals.Thirst);

        // Said only when something is still wrong, so a meal that fixed it ends on the meal.
        if (remaining is not null)
        {
            ctx.Reply($"You are still {remaining}.", "bad");
        }
    }

    /// <summary>How long before another consumable with effects, or null when there is no wait.</summary>
    private static string? StillWaiting(CommandContext ctx, Guid characterId, ItemTemplate template, long now)
    {
        if (ctx.World.GetAbilityCooldown(characterId, ItemTemplate.UseCooldownKey) is not { } last)
        {
            return null;
        }

        var left = last + (template.UseCooldownPulses ?? ItemTemplate.DefaultUseCooldownPulses) - now;

        if (left <= 0)
        {
            return null;
        }

        var seconds = (int)Math.Ceiling(left * GameTiming.PulseInterval.TotalSeconds);
        return $"You could not keep another down yet. Give it {seconds} more second{(seconds == 1 ? "" : "s")}.";
    }

    private static bool AtBest(Vitals vitals) =>
        vitals.Health >= vitals.HealthMax
        && vitals.Focus >= vitals.FocusMax
        && vitals.Stamina >= vitals.StaminaMax;

    /// <summary>What the effects gave back, in numbers, because "you feel better" says nothing.</summary>
    private static string Restored((int Health, int Focus, int Stamina) before, Vitals after)
    {
        var gains = new List<string>();

        if (after.Health > before.Health)
        {
            gains.Add($"+{after.Health - before.Health} health");
        }

        if (after.Focus > before.Focus)
        {
            gains.Add($"+{after.Focus - before.Focus} focus");
        }

        if (after.Stamina > before.Stamina)
        {
            gains.Add($"+{after.Stamina - before.Stamina} stamina");
        }

        return gains.Count == 0
            ? "Nothing seems to happen."
            : $"You feel it take hold: {string.Join(", ", gains)}.";
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
