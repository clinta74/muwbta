using Muwbta.Mcp;

namespace Muwbta.Mcp.Tests;

/// <summary>
/// The kind-to-path table.
/// </summary>
/// <remarks>
/// Worth testing despite being a lookup, because of how its failures present: a wrong path is a
/// 404 the agent reads as "that content does not exist", so it writes the room again rather than
/// reporting a broken tool. The expensive bugs here are the silent ones.
/// </remarks>
public class ContentKindsTests
{
    [Theory]
    [InlineData("configuration", "/api/builder/configurations")]
    [InlineData("world", "/api/builder/worlds")]
    [InlineData("zone", "/api/builder/zones")]
    [InlineData("mob", "/api/builder/mob-templates")]
    [InlineData("item", "/api/builder/item-templates")]
    [InlineData("ability", "/api/builder/abilities")]
    [InlineData("quest", "/api/builder/quests")]
    [InlineData("spawner", "/api/builder/spawners")]
    public void Lists_each_kind_at_its_own_collection(string kind, string expected) =>
        Assert.Equal(expected, ContentKinds.ListPath(kind, world: null, zone: null));

    [Fact]
    public void Lists_rooms_under_their_zone() =>
        Assert.Equal(
            "/api/builder/zones/ald-tavern/rooms",
            ContentKinds.ListPath("room", world: null, zone: "ald-tavern"));

    [Fact]
    public void Refuses_to_list_rooms_without_a_zone() =>
        Assert.Null(ContentKinds.ListPath("room", world: null, zone: null));

    [Fact]
    public void Narrows_zones_by_world_and_spawners_by_zone()
    {
        Assert.Equal("/api/builder/zones?world=aldenmoor", ContentKinds.ListPath("zone", "aldenmoor", null));
        Assert.Equal("/api/builder/spawners?zone=ald-tavern", ContentKinds.ListPath("spawner", null, "ald-tavern"));
    }

    /// <summary>
    /// The server would ignore a stray parameter rather than fail, which is exactly why this is
    /// asserted: a silently ignored filter looks to the agent like a filter that did not work.
    /// </summary>
    [Fact]
    public void Ignores_filters_the_endpoint_does_not_take()
    {
        Assert.Equal("/api/builder/worlds", ContentKinds.ListPath("world", "aldenmoor", "ald-tavern"));
        Assert.Equal("/api/builder/quests", ContentKinds.ListPath("quest", "aldenmoor", "ald-tavern"));
        Assert.Equal("/api/builder/zones", ContentKinds.ListPath("zone", null, "ald-tavern"));
    }

    [Fact]
    public void Gets_a_room_from_its_own_group_not_from_its_zone() =>
        Assert.Equal("/api/builder/rooms/ald-tavern-1", ContentKinds.GetPath("room", "ald-tavern-1"));

    [Fact]
    public void Escapes_keys_and_filters()
    {
        Assert.Equal("/api/builder/quests/a%20b", ContentKinds.GetPath("quest", "a b"));
        Assert.Equal("/api/builder/zones?world=a%20b", ContentKinds.ListPath("zone", "a b", null));
    }

    [Fact]
    public void Has_no_placement_for_things_that_are_not_placed()
    {
        Assert.Equal("/api/builder/mob-templates/rat/placement", ContentKinds.PlacementPath("mob", "rat"));
        Assert.Equal("/api/builder/item-templates/cup/placement", ContentKinds.PlacementPath("item", "cup"));
        Assert.Null(ContentKinds.PlacementPath("quest", "errand"));
    }

    [Fact]
    public void Rejects_a_kind_it_does_not_know()
    {
        Assert.Null(ContentKinds.ListPath("dragon", null, null));
        Assert.Null(ContentKinds.GetPath("dragon", "x"));
    }

    /// <summary>
    /// A configuration is listed but has no single-entity endpoint - the asymmetry the
    /// <c>get_content</c> tool explains rather than 404s on.
    /// </summary>
    [Fact]
    public void Lists_configurations_but_cannot_get_one()
    {
        Assert.NotNull(ContentKinds.ListPath("configuration", null, null));
        Assert.Null(ContentKinds.GetPath("configuration", "aldenmoor"));
    }

    /// <summary>Every kind the tool descriptions advertise has to actually resolve somewhere.</summary>
    [Fact]
    public void Every_advertised_kind_resolves()
    {
        foreach (var kind in ContentKinds.Known.Split(',', StringSplitOptions.TrimEntries))
        {
            Assert.True(
                ContentKinds.ListPath(kind, null, "a-zone") is not null
                || ContentKinds.GetPath(kind, "a-key") is not null,
                $"'{kind}' is advertised in ContentKinds.Known but resolves to no path.");
        }
    }
}
