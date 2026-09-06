using Muwbta.Mcp;

namespace Muwbta.Mcp.Tests;

/// <summary>
/// Which worlds the guard reads as live, and which keys it resolves to a world.
/// </summary>
/// <remarks>
/// The refusal itself needs a server, so it is exercised end to end rather than here. What can be
/// pinned without one is the two pieces of logic that decide it - and both fail in the direction
/// that matters silently: a world the parser misses is a live world an agent may write into.
/// </remarks>
public class WorldGuardTests
{
    private const string TwoConfigurations = """
        {
          "configurations": [
            { "key": "draft", "isActive": false, "worldKeys": ["sketch", "spare"] },
            { "key": "live", "isActive": true, "worldKeys": ["ossara", "grask"] }
          ],
          "activeStartingRoomKey": "ossara.gatetown.the-gate-yard"
        }
        """;

    [Fact]
    public void Reads_the_active_configurations_worlds_and_no_others()
    {
        var worlds = WorldGuard.Parse(TwoConfigurations);

        Assert.Equal(["grask", "ossara"], worlds.OrderBy(w => w, StringComparer.Ordinal));
    }

    [Fact]
    public void Matches_a_world_whatever_its_casing() =>
        Assert.Contains("Ossara", WorldGuard.Parse(TwoConfigurations));

    /// <summary>
    /// A development server usually has nothing active, and the guard is quiet there rather than
    /// refusing every write on a machine where nobody is playing.
    /// </summary>
    [Fact]
    public void Protects_nothing_when_no_configuration_is_active() =>
        Assert.Empty(WorldGuard.Parse("""
            { "configurations": [ { "key": "draft", "isActive": false, "worldKeys": ["sketch"] } ] }
            """));

    [Fact]
    public void Protects_nothing_when_the_active_configuration_claims_no_worlds() =>
        Assert.Empty(WorldGuard.Parse("""
            { "configurations": [ { "key": "live", "isActive": true, "worldKeys": [] } ] }
            """));

    [Fact]
    public void Survives_a_response_without_the_field() =>
        Assert.Empty(WorldGuard.Parse("""{ "configurations": [ { "key": "live", "isActive": true } ] }"""));

    // -----------------------------------------------------------------------
    // Which world a key belongs to
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("world", "ossara", "ossara")]
    [InlineData("zone", "ossara.gatetown", "ossara")]
    [InlineData("room", "ossara.gatetown.the-gate-yard", "ossara")]
    public void Reads_the_world_off_a_world_scoped_key(string kind, string key, string expected) =>
        Assert.Equal(expected, ContentKinds.WorldOfKey(kind, key));

    /// <summary>
    /// The hole in the guard, asserted so it stays a known one. A mob is global: nothing in its
    /// key says which worlds it is spawned in, so the guard cannot protect it and where_used is
    /// what an author has instead.
    /// </summary>
    [Theory]
    [InlineData("mob")]
    [InlineData("item")]
    [InlineData("quest")]
    [InlineData("ability")]
    [InlineData("spawner")]
    public void Cannot_read_a_world_off_a_global_kind(string kind) =>
        Assert.Null(ContentKinds.WorldOfKey(kind, "ossara-rat"));

    [Fact]
    public void Reads_no_world_off_an_empty_key() =>
        Assert.Null(ContentKinds.WorldOfKey("room", ""));
}
