using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Narration;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Commands;

/// <summary>
/// Buying and selling (PLAN.md §5.2c).
/// </summary>
/// <remarks>
/// The caches and options come from the <see cref="CommandContext"/> rather than from statics
/// captured at registration. They were static, which made the last-constructed registry the one
/// every other registry read from - harmless in the server, which builds one, and a race in the
/// tests, which build one per case and run classes in parallel.
/// </remarks>
public static class ShopCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "list", 3, "list - see what a shopkeeper has", ListShop));

        commands.Add(new CommandDefinition(
            "buy", 1, "buy <item> - purchase an item from a shopkeeper", Buy));

        commands.Add(new CommandDefinition(
            "sell", 1, "sell <item> - sell an item to a shopkeeper", Sell));
    }

    /// <summary>
    /// Whether the shop system is wired up and its templates are in hand.
    /// </summary>
    /// <remarks>
    /// All three verbs ask the same question. Only <c>list</c> used to, so before the caches had
    /// loaded a player was told the shop did not exist by one verb and served by the other two.
    /// </remarks>
    private static bool IsShopAvailable(CommandContext ctx) =>
        ctx.MobTemplates is { IsLoaded: true } && ctx.ItemTemplates is not null;

    /// <summary>
    /// Every shopkeeper standing in this room, in the order they are stood in it.
    /// </summary>
    /// <remarks>
    /// A list rather than a single shopkeeper, because a market square is an obvious thing to
    /// build and nothing stops a builder putting two spawners in one room. Binding the verbs to
    /// the first match made the second shop unreachable: there is no syntax for naming a
    /// shopkeeper, so "buy bread" beside a smith and a baker refused the bread. Worse, the match
    /// shifted as mobs wandered in and out, so which shop a room *had* was not stable.
    /// </remarks>
    private static List<MobTemplate> ShopkeepersIn(CommandContext ctx)
    {
        if (ctx.MobTemplates is null)
        {
            return [];
        }

        return [.. ctx.World.MobsIn(ctx.Actor.Character.RoomKey)
            .Select(mob => ctx.MobTemplates.Get(mob.TemplateKey))
            .Where(template => MobBehavior.IsShopkeeper(template?.Behavior))
            .OfType<MobTemplate>()];
    }

    private static void ListShop(CommandContext ctx)
    {
        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var shopkeepers = ShopkeepersIn(ctx);
        if (shopkeepers.Count == 0)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Every shop, not just the first. A player standing in a market square can see there are
        // two traders in front of them; a listing that covered one of them would read as a bug.
        foreach (var template in shopkeepers)
        {
            var stock = MobBehavior.SellsOf(template.Behavior);
            if (stock.Count == 0)
            {
                ctx.Reply(
                    $"{NarrationHelper.WithDefiniteArticle(template.Name, capitalize: true)} has nothing to sell.");
                continue;
            }

            ctx.Reply($"=== {template.Name}'s Shop ===");

            foreach (var itemKey in stock)
            {
                var itemTemplate = ctx.ItemTemplates?.Get(itemKey);
                if (itemTemplate is null)
                {
                    // The shop lists a template that no longer exists. Saying so beats a short
                    // list the builder cannot account for - the shop equivalent of a dangling exit.
                    //
                    // The key used to be in this line, which made it the one place an ordinary
                    // customer could read an authoring identifier off a shelf (BUGS.md #14). A
                    // builder still gets it, through the same gate every other key goes through.
                    ctx.Reply("  (an empty space where something should be)", "dim");

                    if (ctx.Actor.IsBuilder)
                    {
                        ctx.Reply($"    missing template: {itemKey}", "dim");
                    }
                    continue;
                }

                var price = ShopPricing.Price(itemTemplate.BaseValue, MobBehavior.MarkupOf(template.Behavior));
                ctx.Reply($"{itemTemplate.Icon} {itemTemplate.Name}: {price} gold");
                AppendComparison(ctx, itemTemplate);
            }
        }
    }

    /// <summary>
    /// What the item on the shelf does, and how it compares to what the customer is wearing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A shop listing used to be a name and a price.</b> Nothing anywhere would tell you what a
    /// weapon hit for before you owned it — <c>examine</c> reaches your pack, the floor and the
    /// people in the room, and shop stock is none of those: it is a template that has never been
    /// spawned. So the only way to find out whether a sword beat the one on your hip was to buy it.
    /// </para>
    /// <para>
    /// <b>The comparison is the part that was asked for.</b> Slot and stats alone still leave the
    /// player doing arithmetic across two screens; naming the piece it would replace and what
    /// moves is the answer to "is this better than mine". It stops short of saying <em>better</em>,
    /// because three more damage for one less accuracy is a trade and calling that an upgrade
    /// would be guessing on their behalf.
    /// </para>
    /// <para>
    /// <b>Base stats against resolved ones is a fair comparison, and only by luck.</b>
    /// <c>ItemSpawner</c> copies <c>BaseStats</c> into <c>ResolvedStats</c> untouched — only
    /// <c>Value</c> is multiplied — so what is on the shelf is what you would be carrying. If item
    /// stats ever start being scaled at spawn, this has to resolve the template through the room's
    /// zone first or it will quietly quote the wrong numbers.
    /// </para>
    /// </remarks>
    private static void AppendComparison(CommandContext ctx, ItemTemplate stock)
    {
        // A draught has no slot and no stats, and what it does is the whole reason to buy it - so
        // it is the one item without numbers that the listing does not stay quiet about.
        if (ItemUse.EffectsProse(stock) is { } effects)
        {
            ctx.Reply($"    {(stock.DrinkValue is > 0 ? "drink" : "eat")}: {effects}", "dim");
        }

        var stats = ItemStatLine.For(stock.BaseStats);
        var slots = SlotRules.Normalize(stock.Slots);

        if (stats is null && slots.Count == 0)
        {
            // A rope, a lamp, a loaf. The price is the whole story and a blank line under it is
            // noise on a listing that may be twenty items long.
            return;
        }

        var where = slots.Count == 0
            ? null
            : string.Join(" or ", slots.Select(SlotRules.Name));

        ctx.Reply(
            "    " + string.Join(" - ", new[] { where, stats }.Where(part => part is not null)),
            "dim");

        if (slots.Count == 0)
        {
            return;
        }

        // The first of the slots it could go in that already holds something. A blade for either
        // hand is compared against whichever hand is occupied, which is the swap a player is
        // actually weighing.
        var equipped = ctx.World.EquipmentOf(ctx.Actor.CharacterId);
        var worn = slots
            .Select(slot => equipped.FirstOrDefault(i => i.EquippedSlot == slot))
            .FirstOrDefault(i => i is not null);

        if (worn is null)
        {
            ctx.Reply("    You have nothing there.", "dim");
            return;
        }

        var delta = ItemStatLine.Delta(stock.BaseStats, worn.ResolvedStats);

        ctx.Reply(
            delta is null
                ? $"    No different from your {worn.DisplayName}."
                : $"    Against your {worn.DisplayName}: {delta}.",
            "dim");
    }

    private static void Buy(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Buy what?");
            return;
        }

        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        var shopkeepers = ShopkeepersIn(ctx);
        if (shopkeepers.Count == 0)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Ask every shop in the room, not just the first. The player named an item, not a
        // trader - and there is no syntax for naming one - so "buy bread" has to find the baker
        // even when the smith happens to be standing nearer the door.
        var (shopkeeperTemplate, itemKey) = shopkeepers
            .Select(shop => (
                Shop: shop,
                // Matched on the stocked item's display name as well as its key, so "buy bread"
                // works against a "loaf-of-bread" key the player has never seen.
                Key: NameMatch.Best(
                    MobBehavior.SellsOf(shop.Behavior),
                    itemName,
                    k => ctx.ItemTemplates?.Get(k)?.Name,
                    k => k)))
            .FirstOrDefault(match => match.Key is not null);

        if (itemKey is null)
        {
            // "Nothing here sells that" rather than naming one shop, which would be misleading
            // when several are present and the player cannot tell which one answered.
            ctx.Reply(
                shopkeepers.Count == 1
                    ? $"{shopkeepers[0].Name} doesn't have that."
                    : "None of the shopkeepers here has that.");
            return;
        }

        var itemTemplate = ctx.ItemTemplates?.Get(itemKey);
        if (itemTemplate is null)
        {
            ctx.Reply("The shopkeeper doesn't have that.");
            return;
        }

        // Priced by the shop that stocks it, not by the first one in the room: the markup belongs
        // to the trader, so a smith beside a baker must not sell bread at the smith's prices.
        // `shopkeeperTemplate` is the shop the match came from, which is what makes this hold.
        var price = ShopPricing.Price(
            itemTemplate.BaseValue, MobBehavior.MarkupOf(shopkeeperTemplate.Behavior));

        if (character.Gold < price)
        {
            ctx.Reply($"You need {price} gold, but only have {character.Gold}.");
            return;
        }

        // Refused before the gold is taken. A shop is the one place an item can be acquired
        // repeatedly and on demand, so leaving lore out here would make it the loophole rather
        // than an oversight.
        if (itemTemplate.IsLore &&
            ItemRules.AlreadyHolds(ctx.World, character.Id, itemTemplate.Key))
        {
            ctx.Reply($"You already have {NarrationHelper.WithArticle(itemTemplate.Name)}.", "bad");
            return;
        }

        // Spawn the item. The lookup wants the qualified "world.zone" key - RoomKey.Zone is the
        // bare segment, so this always missed and every purchase died on the guard below
        // reporting the shop temporarily unavailable.
        var zone = ctx.World.FindZone(character.RoomKey.ZoneKey);
        var world = zone is not null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone is null || world is null)
        {
            ctx.Reply("The shop is temporarily unavailable.");
            return;
        }

        // The spawner's instance is used directly. This used to spawn one and then hand-build a
        // second from its parts, which was identical field for field - and would now silently
        // drop the questItem stamp the spawner applies.
        var instance = new ItemSpawner().Spawn(itemTemplate, zone, world, character.RoomKey);

        ctx.World.AddItem(instance);
        ctx.World.PickUpItem(instance, character.Id);
        ctx.ItemSaveQueue?.Enqueue(instance);

        // Deduct gold
        character.Gold -= price;

        ctx.Reply($"You buy {NarrationHelper.WithArticle(itemTemplate.Name)} for {price} gold.", "success");
        ctx.Broadcast(
            $"{character.Name} buys something from {NarrationHelper.WithArticle(shopkeeperTemplate.Name)}.",
            "activity");
    }

    private static void Sell(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Sell what?");
            return;
        }

        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        // Any shopkeeper will take it: sellback is a flat share of the item's value, so which
        // one buys it is not observable. Stock lists are what a shop sells, not what it accepts.
        // A markup does not reach here either (§4.13) - it is a buy-side dial, so "expensive to
        // buy from" and "pays well" stay two different things a builder sets independently, and
        // the choice of which trader takes the item stays unobservable with markups in play.
        var shopkeeperTemplate = ShopkeepersIn(ctx).FirstOrDefault();
        if (shopkeeperTemplate is null)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Find the item in inventory
        var inventory = ctx.World.InventoryOf(character.Id);
        var itemToSell = NameMatch.Best(
            inventory, itemName, i => i.TemplateName, i => i.TemplateKey);

        if (itemToSell is null)
        {
            ctx.Reply("You don't have that.");
            return;
        }

        // InventoryOf returns equipped items too, so without this you can sell the sword in your
        // hand - which `destroy` has always refused. Same sentence, so the four exits agree.
        if (itemToSell.EquippedSlot is not null)
        {
            ctx.Reply(
                $"You'll have to remove {NarrationHelper.WithDefiniteArticle(itemToSell.DisplayName)} first.",
                "bad");
            return;
        }

        if (ItemState.IsQuestItem(itemToSell))
        {
            ctx.Reply(
                $"{NarrationHelper.WithArticle(shopkeeperTemplate.Name, capitalize: true)} refuses to buy a quest item.",
                "bad");
            return;
        }

        // A shop was an unsanctioned way out of a no-drop item that also paid for it, while `drop`
        // was telling the player that destroying it was the only way to be rid of it (BUGS.md #9).
        if (ItemRules.IsNoDrop(ctx.ItemTemplates, itemToSell.TemplateKey))
        {
            ctx.Reply(
                $"{NarrationHelper.WithDefiniteArticle(itemToSell.DisplayName, capitalize: true)} "
                + "will not leave your hand. You could destroy it, if you truly meant to.",
                "bad");
            return;
        }

        // Calculate sellback price
        var sellbackPercent = ctx.Options?.ShopSellbackPercent ?? 0.5m;
        var sellPrice = (long)(itemToSell.Value * sellbackPercent);

        // Remove item and credit gold. The delete has to reach storage too, or the row
        // outlives the sale still pointing at its former owner and the item comes back in
        // the player's inventory on restart.
        ctx.World.RemoveItem(itemToSell);
        ctx.ItemSaveQueue?.EnqueueDelete(itemToSell.Id);
        character.Gold += sellPrice;

        ctx.Reply(
            $"You sell {NarrationHelper.WithDefiniteArticle(itemToSell.DisplayName)} for {sellPrice} gold.",
            "success");
        ctx.Broadcast(
            $"{character.Name} sells something to {NarrationHelper.WithArticle(shopkeeperTemplate.Name)}.",
            "activity");
    }
}
