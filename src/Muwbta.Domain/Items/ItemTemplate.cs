using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Items;

/// <summary>
/// PLAN.md §4.8: A baseline item definition. Global, not zone-scoped. One template appears
/// at many power tiers via zone multipliers. Never mutated; spawned instances carry the
/// multiplier-resolved stats.
/// </summary>
public sealed class ItemTemplate
{
    /// <summary>Single lowercase segment, e.g. "rusty-dagger".</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Single character icon for the map display.</summary>
    public required string Icon { get; set; }

    /// <summary>Equipment slots this may fill. Empty means it is not equippable at all.</summary>
    /// <remarks>
    /// <para>
    /// A list rather than one slot, because "either hand" is a real property of a weapon and there
    /// was no way to say it: every one of the 35 authored weapons was <c>MainHand</c>, all six
    /// off-hand items were shields or a torch, and so the whole off-hand combat path -
    /// <c>DualWield</c> from level 3, <c>Ambidextrous</c>, <c>OffHandDamageShare</c>, the second
    /// strike in <c>CombatSystem</c> - had nothing in the world that could reach it.
    /// </para>
    /// <para>
    /// <b>Empty means nowhere</b>, which is the opposite of <see cref="Paths"/>, where empty means
    /// anyone. The two read alike and mean opposite things, so they are worth keeping straight: a
    /// slot list is a capability and starts at none, a Path list is a restriction and starts at no
    /// restriction. Both defaults are "the item as authored does the unsurprising thing".
    /// </para>
    /// <para>
    /// Held in enum order, which is also the order the hands are tried in - so an either-hand
    /// weapon reaches for the main hand first without anything having to say so separately.
    /// </para>
    /// </remarks>
    public List<ItemSlot> Slots { get; set; } = [];

    /// <summary>Whether wielding this claims the off hand as well as the main one.</summary>
    /// <remarks>
    /// Not expressible as a slot list: a two-handed weapon does not go in two places, it goes in
    /// one and denies the other. Only meaningful with <see cref="ItemSlot.MainHand"/>, and the
    /// builder refuses any other combination rather than normalising it quietly -
    /// <c>slots: [OffHand], twoHanded: true</c> is a mistake, not a shorthand.
    /// </remarks>
    public bool IsTwoHanded { get; set; }

    /// <summary>Weight in grams, for encumbrance calculation.</summary>
    public int Weight { get; set; }

    /// <summary>Base vendor value before multipliers. Persisted as jsonb.</summary>
    public int BaseValue { get; set; }

    /// <summary>
    /// Base stats before multipliers: damage dice, armor values, attribute bonuses.
    /// Persisted as jsonb object (free-form, validated by game logic only).
    /// </summary>
    public Dictionary<string, object> BaseStats { get; set; } = new();

    /// <summary>
    /// Pulses between swings when this is wielded. Null means it declares no speed: in the main
    /// hand that is the 8-pulse default, and in the off hand it means the item is not a weapon at
    /// all and never strikes - which is what keeps shields from punching.
    /// </summary>
    /// <remarks>
    /// A column rather than a <see cref="BaseStats"/> key on purpose. The builder coerces every
    /// base stat to a number, which would destroy a verb, and a floor of 4 needs the server to be
    /// able to refuse a save.
    /// </remarks>
    public int? AttackDelayPulses { get; set; }

    /// <summary>
    /// Base-form verb describing how this weapon strikes: "slash" for a sword, "crush" for a club.
    /// Null narrates as "hit". See <see cref="Narration.NarrationHelper.ThirdPerson"/>.
    /// </summary>
    public string? AttackVerb { get; set; }

    /// <summary>
    /// Marks instances of this template as bound to a quest: they cannot be sold or destroyed,
    /// but can still be dropped (PLAN.md §4.9).
    /// </summary>
    /// <remarks>
    /// A column rather than a <see cref="BaseStats"/> key, for the same reason as
    /// <see cref="AttackDelayPulses"/>: it is a rule the server enforces, not a number to scale.
    /// The flag is stamped onto each instance at spawn time by <c>ItemSpawner</c> rather than read
    /// from the template at the point of sale, so an item already in a pack keeps the rule it was
    /// created under even if a builder later unsets it.
    /// </remarks>
    public bool IsQuestItem { get; set; }

    /// <summary>
    /// One only. A character may not hold a second copy in inventory or equipment.
    /// </summary>
    /// <remarks>
    /// The rule an epic reward needs: without it one player farms an act boss and equips five
    /// friends. Checked wherever an item enters a pack — picking it up, buying it, being given it,
    /// and being handed it as a quest reward — because a lore item that only the loot path refuses
    /// is a lore item you buy two of.
    /// </remarks>
    public bool IsLore { get; set; }

    /// <summary>
    /// Cannot be dropped or given away. It <em>can</em> be destroyed.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsQuestItem"/>, which is the opposite pair: a quest item cannot be
    /// sold or destroyed but can be put down. This one is about who ends up holding it rather than
    /// about it surviving, so the way out is deliberate destruction — leaving no way at all to be
    /// rid of a bound item is how a pack fills up with things a character will never use again.
    /// </remarks>
    public bool IsNoDrop { get; set; }

    /// <summary>
    /// Lights the room it is worn or wielded in.
    /// </summary>
    /// <remarks>
    /// <b>Any slot, not a slot of its own.</b> A lantern in the off hand, a helm with a lamp on it,
    /// a pendant that glows — the fiction is the item's, and reserving a hand for light would make
    /// every dark room a choice between seeing and carrying a shield. Carrying one in the pack is
    /// deliberately not enough: a light you have not taken out is a light that is not lit.
    ///
    /// A column rather than a <see cref="BaseStats"/> key, for the reason
    /// <see cref="AttackDelayPulses"/> gives — the builder coerces every base stat to a number, and
    /// this is a rule rather than a quantity. It is read from the template rather than stamped onto
    /// the instance, so a builder who lights a lamp lights the ones already out in the world too.
    /// </remarks>
    public bool IsLightSource { get; set; }

    /// <summary>
    /// How much hunger eating this answers, or null when it is not food.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null rather than zero, because they say different things.</b> Null means "not edible" and
    /// is what nearly every item in the game is; zero would mean "edible, and worth nothing", which
    /// is an item worth authoring only as a joke. <c>eat</c> refuses anything with no value here.
    /// </para>
    /// <para>
    /// A column rather than a <see cref="BaseStats"/> key, and this one is not a close call: the bag
    /// is closed against <c>EquipmentResolver.KnownStatKeys</c> by <c>BundleValidator</c>, which is a
    /// combat vocabulary — a bundle carrying <c>foodValue</c> in the bag is refused on import.
    /// </para>
    /// <para>
    /// Separate from <see cref="DrinkValue"/> so a loaf can be food, a skin can be drink, and a stew
    /// or an ale can be both. One field with a kind beside it would have made the third case need a
    /// second item.
    /// </para>
    /// </remarks>
    public int? FoodValue { get; set; }

    /// <summary>How much thirst drinking this answers, or null when it is not drink.</summary>
    /// <inheritdoc cref="FoodValue" path="/remarks"/>
    public int? DrinkValue { get; set; }

    /// <summary>What <see cref="UseCooldownPulses"/> means when an item leaves it blank: 30 seconds.</summary>
    public const int DefaultUseCooldownPulses = 120;

    /// <summary>The one cooldown every consumable with effects shares.</summary>
    public const string UseCooldownKey = "item.use";

    /// <summary>
    /// What eating or drinking this does besides answering hunger or thirst: ability effects, run
    /// on whoever consumes it. Empty for nearly everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A potion is a drink with effects</b>, not a new kind of item. It is still consumed by
    /// <c>drink</c> (or <c>eat</c>), gated on <see cref="DrinkValue"/> or <see cref="FoodValue"/>
    /// as before, so a healing draught is authored with a drink value of one and its effect beside
    /// it. From playtesting: the only way back from a hard fight was to sit and wait, and this is
    /// downtime a player can now buy their way out of, at a price and on a timer.
    /// </para>
    /// <para>
    /// <b>The abilities' own effects.</b> <c>heal.restore</c> and <c>resource.restore</c> already
    /// refill health, focus and stamina by an amount or a share of the bar, and a second vocabulary
    /// for the same thing would drift from the first. Harmless effects only - a thing you drink has
    /// nobody to hurt, and a harmful effect wants a target and a fight - so <see cref="ItemUse"/>
    /// refuses the rest, on save and in the bundle validator alike.
    /// </para>
    /// </remarks>
    public List<AbilityEffectSpec> UseEffects { get; set; } = [];

    /// <summary>
    /// Pulses before anything else with effects can be consumed, or null for
    /// <see cref="DefaultUseCooldownPulses"/>.
    /// </summary>
    /// <remarks>
    /// <b>One timer for every consumable</b> (<see cref="UseCooldownKey"/>): a healing draught makes
    /// a stamina tonic wait too. Separate timers would make a pack of different potions a fight with
    /// no downtime at all, which is the opposite of what a price was meant to buy.
    /// </remarks>
    public int? UseCooldownPulses { get; set; }

    /// <summary>
    /// The Paths that may wear or wield this. Empty means anyone.
    /// </summary>
    /// <remarks>
    /// Empty rather than null-or-all, so the default state of an authored item is "no restriction"
    /// and a builder has to opt in. A list rather than a single Path because the useful case is
    /// "the two martial Paths" as often as it is one.
    ///
    /// This restricts <em>equipping</em> only. Carrying, selling and handing over are deliberately
    /// left alone: a Temper should be able to pick up a Warden's shield and take it to the Warden.
    /// </remarks>
    public List<CharacterPath> Paths { get; set; } = [];
}

/// <summary>Equippable item slots.</summary>
public enum ItemSlot
{
    Head,
    Chest,
    Hands,
    Legs,
    Feet,
    MainHand,
    OffHand,
    Trinket,
}
