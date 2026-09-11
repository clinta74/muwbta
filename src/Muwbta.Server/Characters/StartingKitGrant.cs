using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;

namespace Muwbta.Server.Characters;

/// <summary>
/// Turns a configuration's starting kit into the item rows a new character owns.
/// </summary>
/// <remarks>
/// An item in a pack is a row whose owner is the character, and login loads a character's items by
/// owner, so granting a kit is only writing those rows beside the character. Nothing here runs in
/// the game loop: the character does not exist there until it first logs in.
///
/// Not <c>ItemSpawner</c>, which resolves value through a zone's multipliers - a pack is in no zone,
/// and a kit that is no-drop cannot be sold anyway. What it does share with the spawner is the
/// quest-item stamp, because that is a rule the item carries from the moment it exists.
/// </remarks>
public static class StartingKitGrant
{
    /// <summary>
    /// The items a character is handed. Lines naming a template this server does not have are
    /// skipped rather than refused: the kit was checked when it was saved, and a template deleted
    /// since then should cost a new player one item, not their character.
    /// </summary>
    public static IReadOnlyList<ItemInstance> ItemsFor(
        IEnumerable<StartingKitItem> kit,
        IReadOnlyDictionary<string, ItemTemplate> templates,
        Guid characterId)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(templates);

        var items = new List<ItemInstance>();

        foreach (var entry in kit)
        {
            if (!templates.TryGetValue(entry.ItemKey, out var template))
            {
                continue;
            }

            var count = Math.Clamp(entry.Count, 0, GameConfiguration.MaxStartingKitCount);

            for (var i = 0; i < count; i++)
            {
                items.Add(new ItemInstance
                {
                    TemplateKey = template.Key,
                    TemplateName = string.IsNullOrEmpty(template.Name) ? template.Key : template.Name,
                    Icon = template.Icon,
                    OwnerCharacterId = characterId,
                    ResolvedStats = new Dictionary<string, object>(template.BaseStats),
                    Value = template.BaseValue,
                    State = template.IsQuestItem
                        ? new Dictionary<string, object> { ["questItem"] = true }
                        : [],
                });
            }
        }

        return items;
    }
}
