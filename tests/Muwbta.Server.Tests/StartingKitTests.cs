using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence;
using Muwbta.Persistence.Seeding;
using Muwbta.Server.Characters;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// A new character is handed what the live configuration says to hand them (PLAN.md §4.16).
/// </summary>
/// <remarks>
/// From playtesting: a character started with nothing at all, and the first fight was taken with
/// bare hands. The kit is the configuration's rather than the engine's because what fits is a
/// question about the world.
/// </remarks>
public sealed class StartingKitTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static ItemTemplate Template(string key, string name, bool questItem = false) => new()
    {
        Key = key,
        Name = name,
        Icon = "i",
        BaseValue = 4,
        IsQuestItem = questItem,
        BaseStats = new Dictionary<string, object> { ["damageMax"] = 3 },
    };

    [Fact]
    public void Each_line_is_handed_out_as_many_times_as_it_says()
    {
        var templates = new Dictionary<string, ItemTemplate>
        {
            ["bread"] = Template("bread", "a heel of bread"),
            ["knife"] = Template("knife", "a worn knife"),
        };

        var items = StartingKitGrant.ItemsFor([new("knife"), new("bread", 2)], templates, Owner);

        Assert.Equal(3, items.Count);
        Assert.Equal(2, items.Count(i => i.TemplateKey == "bread"));
        Assert.All(items, i => Assert.Equal(Owner, i.OwnerCharacterId));
        Assert.All(items, i => Assert.Null(i.RoomKey));
        Assert.Contains(items, i => i.DisplayName == "a worn knife" && i.ResolvedStats.ContainsKey("damageMax"));
    }

    /// <summary>A template deleted since the kit was saved costs the player one item, not the character.</summary>
    [Fact]
    public void A_line_naming_no_template_is_skipped()
    {
        var templates = new Dictionary<string, ItemTemplate> { ["bread"] = Template("bread", "a heel of bread") };

        var items = StartingKitGrant.ItemsFor([new("gone"), new("bread")], templates, Owner);

        Assert.Equal("bread", Assert.Single(items).TemplateKey);
    }

    [Fact]
    public void A_quest_item_in_a_kit_is_still_a_quest_item()
    {
        var templates = new Dictionary<string, ItemTemplate> { ["letter"] = Template("letter", "a letter", questItem: true) };

        var item = Assert.Single(StartingKitGrant.ItemsFor([new("letter")], templates, Owner));

        Assert.Equal(true, item.State["questItem"]);
    }

    /// <summary>The seeded kit names items the seed plants, and none of them can be sold on.</summary>
    [Fact]
    public void Aldenmoors_kit_is_seeded_and_bound()
    {
        Assert.NotEmpty(StarterWorldSeeder.StarterKit);

        foreach (var entry in StarterWorldSeeder.StarterKit)
        {
            var template = Assert.Single(StarterWorldSeeder.ItemTemplates, t => t.Key == entry.ItemKey);
            Assert.True(template.IsNoDrop, $"{entry.ItemKey} should be no-drop");
        }
    }
}

/// <summary>The kit end to end: a character made through the API owns the items afterwards.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class StartingKitEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_new_character_owns_the_live_configurations_kit()
    {
        using var client = postgres.App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var username = UniqueName("kit");

        (await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{username}@example.test",
            username,
            password = "correcthorse",
        })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/characters", new { name = UniqueName("kit"), path = "Warden" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var characterId = body.RootElement.GetProperty("id").GetGuid();

        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var kit = await db.GameConfigurations.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.StartingKit)
            .SingleAsync();

        Assert.NotEmpty(kit);

        var owned = await db.ItemInstances.AsNoTracking()
            .Where(i => i.OwnerCharacterId == characterId)
            .ToListAsync();

        foreach (var entry in kit)
        {
            Assert.Equal(entry.Count, owned.Count(i => i.TemplateKey == entry.ItemKey));
        }

        Assert.Equal(kit.Sum(k => k.Count), owned.Count);
    }

    /// <summary>Letters only: names are validated against ^[A-Za-z]{3,16}$.</summary>
    private static string UniqueName(string prefix)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return prefix + new string([.. bytes.Take(6).Select(b => (char)('a' + (b % 26)))]);
    }
}
