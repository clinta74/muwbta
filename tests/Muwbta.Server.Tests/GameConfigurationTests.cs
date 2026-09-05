using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Persistence;
using Muwbta.Persistence.Seeding;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// Named starter configurations (PLAN.md §4.16): where a new character wakes up and what they are
/// told, as content a builder edits rather than as a deploy-time setting.
/// </summary>
/// <remarks>
/// Through the real HTTP stack and the real loop, because the property that matters is not that a
/// row was written — it is that activating one moves what the running engine obeys. A test that
/// asserted on the table alone would pass while the server kept starting people in Millbrook,
/// which is the exact failure this feature exists to end.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class GameConfigurationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private RoomKey _startingRoom;
    private string _welcome = string.Empty;
    private string _canon = string.Empty;

    /// <summary>
    /// Snapshots the engine's starting room, and puts it back afterwards.
    /// </summary>
    /// <remarks>
    /// This suite is the only one that deliberately moves global engine state, and the fixture is
    /// shared with every other end-to-end class. Without this, activating a configuration here
    /// left every later test creating characters in whichever room this one happened to name -
    /// which is exactly what happened: eight unrelated combat and admin tests failed at once, none
    /// of them anywhere near this feature.
    /// </remarks>
    public Task InitializeAsync()
    {
        var options = postgres.App.Services.GetRequiredService<EngineOptions>();
        _startingRoom = options.StartingRoom;
        _welcome = options.WelcomeMessage;
        _canon = options.Canon;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        var options = postgres.App.Services.GetRequiredService<EngineOptions>();
        options.StartingRoom = _startingRoom;
        options.WelcomeMessage = _welcome;
        options.Canon = _canon;

        // And the row, so a later restart of the fixture does not reload what this suite chose.
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
        await db.GameConfigurations
            .Where(c => c.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static object Body(string name, string startingRoom, string? welcome = null) => new
    {
        name,
        description = "Authored by a test.",
        startingRoomKey = startingRoom,
        welcomeMessage = welcome ?? "Welcome to the test, {name}.",
    };

    [Fact]
    public async Task Configurations_are_closed_to_ordinary_players()
    {
        // The role is set explicitly rather than trusted to registration. The first account on an
        // empty database becomes Admin so a fresh install is reachable at all, so whether a
        // freshly registered account is an ordinary player depends on which test class the runner
        // happened to start with - and this one failed exactly that way, reporting an open
        // endpoint that was never open.
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Player);

        // Signed in again, because the role rides in the auth cookie as a claim.
        (await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = "correcthorse" })).EnsureSuccessStatusCode();

        var list = await client.GetAsync(new Uri("/api/builder/configurations", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task A_configuration_is_written_read_back_and_listed()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"written-{Guid.NewGuid():N}"[..24];

        var created = await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            Body("Written", "aldenmoor.millbrook.north-gate"));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/builder/configurations", UriKind.Relative));

        var mine = listed.GetProperty("configurations").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == key);

        Assert.Equal("Written", mine.GetProperty("name").GetString());
        Assert.Equal("aldenmoor.millbrook.north-gate", mine.GetProperty("startingRoomKey").GetString());

        // Not live merely by existing. Writing one and choosing it are separate acts.
        Assert.False(mine.GetProperty("isActive").GetBoolean());
    }

    /// <summary>
    /// The canon belongs to the configuration (PLAN.md §4.16): written with it, read back with a
    /// token estimate, handed out as markdown, and moved into the running engine by activation -
    /// which is what makes the assist read it with no restart.
    /// </summary>
    [Fact]
    public async Task A_canon_is_written_estimated_served_and_activated()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"canon-{Guid.NewGuid():N}"[..24];
        const string canon = "# Elsewhere\n\nThere is one Reach and it is round.\n";

        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            new
            {
                name = "Elsewhere",
                startingRoomKey = "aldenmoor.millbrook.north-gate",
                canon,
            }));

        Assert.Equal(canon, created.GetProperty("canon").GetString());
        Assert.True(created.GetProperty("canonTokens").GetInt32() > 0);

        // A save that says nothing about the canon leaves it alone.
        var kept = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            new { name = "Elsewhere, renamed", startingRoomKey = "aldenmoor.millbrook.north-gate" }));
        Assert.Equal(canon, kept.GetProperty("canon").GetString());

        var served = await client.GetAsync(new Uri($"/api/builder/configurations/{key}/canon", UriKind.Relative));
        served.EnsureSuccessStatusCode();
        Assert.StartsWith("text/markdown", served.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Equal(canon, await served.Content.ReadAsStringAsync());

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/builder/configurations", UriKind.Relative));
        Assert.True(list.GetProperty("canonTokenBudget").GetInt32() > 0);

        (await client.PostAsync(new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        Assert.Equal(canon, factory.Services.GetRequiredService<EngineOptions>().Canon);
    }

    /// <summary>
    /// A configuration tags the worlds it is for, and a world belongs to one configuration.
    /// </summary>
    [Fact]
    public async Task Worlds_are_tagged_and_a_world_belongs_to_one_configuration()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (worldKey, _) = await BuilderClient.NewZoneAsync(client);

        var first = $"owner-{Guid.NewGuid():N}"[..24];
        var created = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            $"/api/builder/configurations/{first}",
            new { name = "Owner", startingRoomKey = "aldenmoor.millbrook.north-gate", worldKeys = new[] { worldKey } }));

        Assert.Equal([worldKey], created.GetProperty("worldKeys").EnumerateArray().Select(w => w.GetString()));

        // A save that says nothing about the worlds leaves them alone.
        var kept = await BuilderClient.JsonAsync(await client.PostAsJsonAsync(
            $"/api/builder/configurations/{first}",
            new { name = "Owner, renamed", startingRoomKey = "aldenmoor.millbrook.north-gate" }));
        Assert.Single(kept.GetProperty("worldKeys").EnumerateArray());

        var second = $"rival-{Guid.NewGuid():N}"[..24];
        var refused = await client.PostAsJsonAsync(
            $"/api/builder/configurations/{second}",
            new { name = "Rival", startingRoomKey = "aldenmoor.millbrook.north-gate", worldKeys = new[] { worldKey } });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(first, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var badKey = await client.PostAsJsonAsync(
            $"/api/builder/configurations/{second}",
            new { name = "Rival", startingRoomKey = "aldenmoor.millbrook.north-gate", worldKeys = new[] { "Not A Key" } });
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
    }

    [Fact]
    public async Task A_configuration_may_name_a_room_that_does_not_exist_yet()
    {
        // The ordinary order of operations on a fresh server is to write the configuration and
        // then import the world it points into. Refusing here would make that impossible, so the
        // panel is told rather than stopped (§7.4).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"ahead-{Guid.NewGuid():N}"[..24];

        var created = await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            Body("Ahead of the world", "nowhere.no-zone.no-room"));

        Assert.True(
            created.StatusCode == HttpStatusCode.OK,
            $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("startingRoomExists").GetBoolean());
    }

    [Fact]
    public async Task A_key_that_is_not_a_room_key_is_refused()
    {
        // The one thing that cannot become valid by importing anything later.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var refused = await client.PostAsJsonAsync(
            $"/api/builder/configurations/bad-{Guid.NewGuid():N}"[..30],
            Body("Bad room", "not-three-segments"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Activating_one_moves_what_the_running_engine_obeys()
    {
        // The property the whole feature is for. Asserted on EngineOptions rather than on the row,
        // because a row that the loop never reads is exactly the bug being fixed.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var options = factory.Services.GetRequiredService<EngineOptions>();
        var before = options.StartingRoom;

        var key = $"live-{Guid.NewGuid():N}"[..24];
        await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            Body("Live", "aldenmoor.millbrook.market-square", "Stand up, {name}."));

        // Writing it changes nothing about the running server.
        Assert.Equal(before, options.StartingRoom);

        var activated = await client.PostAsync(
            new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.Equal("aldenmoor.millbrook.market-square", options.StartingRoom.ToString());
        Assert.Equal("Stand up, {name}.", options.WelcomeMessage);
    }

    [Fact]
    public async Task Only_one_configuration_is_live_at_a_time()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var first = $"one-{Guid.NewGuid():N}"[..24];
        var second = $"two-{Guid.NewGuid():N}"[..24];

        await client.PostAsJsonAsync($"/api/builder/configurations/{first}",
            Body("First", "aldenmoor.millbrook.north-gate"));
        await client.PostAsJsonAsync($"/api/builder/configurations/{second}",
            Body("Second", "aldenmoor.millbrook.market-square"));

        // Both asserted. The second one failing silently is precisely how the per-statement
        // uniqueness check first showed up, and an unchecked activate hid it behind a stale list.
        var one = await client.PostAsync(
            new Uri($"/api/builder/configurations/{first}/activate", UriKind.Relative), null);
        Assert.True(one.IsSuccessStatusCode, await one.Content.ReadAsStringAsync());

        var two = await client.PostAsync(
            new Uri($"/api/builder/configurations/{second}/activate", UriKind.Relative), null);
        Assert.True(two.IsSuccessStatusCode, await two.Content.ReadAsStringAsync());

        var listed = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/builder/configurations", UriKind.Relative));

        var active = listed.GetProperty("configurations").EnumerateArray()
            .Where(c => c.GetProperty("isActive").GetBoolean())
            .Select(c => c.GetProperty("key").GetString())
            .ToList();

        Assert.Equal([second], active);
    }

    [Fact]
    public async Task The_live_configuration_cannot_be_deleted()
    {
        // Deleting it would leave the loop obeying values with no row behind them - fine until the
        // next restart, then silently back to the compiled fallback.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"kept-{Guid.NewGuid():N}"[..24];
        await client.PostAsJsonAsync($"/api/builder/configurations/{key}",
            Body("Kept", "aldenmoor.millbrook.north-gate"));
        await client.PostAsync(new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null);

        var refused = await client.DeleteAsync(
            new Uri($"/api/builder/configurations/{key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task An_inactive_configuration_can_be_deleted()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"gone-{Guid.NewGuid():N}"[..24];
        await client.PostAsJsonAsync($"/api/builder/configurations/{key}",
            Body("Gone", "aldenmoor.millbrook.north-gate"));

        var deleted = await client.DeleteAsync(
            new Uri($"/api/builder/configurations/{key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Configurations_travel_in_a_bundle_but_never_which_one_is_live()
    {
        // The half of the design that keeps an import safe on a production server: a starter set
        // moves between environments, and loading it cannot repoint where every new character
        // wakes up as a side effect.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = $"travels-{Guid.NewGuid():N}"[..24];
        await client.PostAsJsonAsync($"/api/builder/configurations/{key}",
            Body("Travels", "aldenmoor.millbrook.north-gate"));
        await client.PostAsync(new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null);

        var exported = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/builder/export", UriKind.Relative));

        var carried = exported.GetProperty("configurations").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == key);

        Assert.Equal("Travels", carried.GetProperty("name").GetString());
        Assert.Equal("aldenmoor.millbrook.north-gate", carried.GetProperty("startingRoomKey").GetString());

        // The definition travels; the choice does not.
        Assert.False(carried.TryGetProperty("isActive", out _));
    }

    [Fact]
    public async Task The_starter_configuration_matches_the_engine_fallback()
    {
        // The point of seeding it at all. EngineOptions carries a compiled default so a database
        // with nothing in it still has somewhere to put people; the row makes that value visible
        // in the Setup tab and editable, instead of a number only the source knows. If the two
        // ever disagree, the panel is lying about where new characters start.
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var seeded = await db.GameConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == StarterWorldSeeder.ConfigurationKey);

        Assert.NotNull(seeded);
        Assert.Equal(StarterWorldSeeder.StartingRoom.ToString(), seeded.StartingRoomKey);

        // And it carries the greeting GameLoop used to hold as a literal, so the old world still
        // says the old thing - now from a row somebody can edit.
        Assert.Contains(GameConfiguration.NameToken, seeded.WelcomeMessage, StringComparison.Ordinal);
        Assert.Contains("Aldenmoor", seeded.WelcomeMessage, StringComparison.Ordinal);

        // And its own canon, so the assist on a development server drafts Millbrook rather than
        // the Reaches - which is what it did while the embedded text was the only canon there was.
        Assert.Contains("Millbrook", seeded.Canon, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_starter_row_with_no_canon_is_given_one_and_a_written_one_is_kept()
    {
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        await db.GameConfigurations
            .Where(c => c.Key == StarterWorldSeeder.ConfigurationKey)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Canon, string.Empty));

        Assert.False(await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db));

        var refilled = await db.GameConfigurations.AsNoTracking()
            .SingleAsync(c => c.Key == StarterWorldSeeder.ConfigurationKey);
        Assert.Equal(StarterWorldSeeder.StarterCanon, refilled.Canon);

        await db.GameConfigurations
            .Where(c => c.Key == StarterWorldSeeder.ConfigurationKey)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Canon, "# Mine"));

        Assert.False(await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db));

        var kept = await db.GameConfigurations.AsNoTracking()
            .SingleAsync(c => c.Key == StarterWorldSeeder.ConfigurationKey);
        Assert.Equal("# Mine", kept.Canon);

        // Put back, so the fixture's other tests see the seeded state.
        await db.GameConfigurations
            .Where(c => c.Key == StarterWorldSeeder.ConfigurationKey)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Canon, StarterWorldSeeder.StarterCanon));
    }

    [Fact]
    public async Task Reconciling_the_starter_configuration_twice_plants_it_once()
    {
        // It runs on every development boot. A second run must not duplicate the row, and - more
        // importantly - must not reclaim the active flag from whatever the operator has chosen
        // since, or every restart would silently undo their choice of world.
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        Assert.False(await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db));

        var count = await db.GameConfigurations.AsNoTracking()
            .CountAsync(c => c.Key == StarterWorldSeeder.ConfigurationKey);

        Assert.Equal(1, count);
    }

    [Fact]
    public void A_blank_greeting_falls_back_rather_than_greeting_nobody()
    {
        // A builder who clears the box has almost certainly not decided that arriving in the world
        // should be silent.
        Assert.Equal("Welcome back, Kael.", GameConfiguration.Greet(null, "Kael"));
        Assert.Equal("Welcome back, Kael.", GameConfiguration.Greet("   ", "Kael"));
        Assert.Equal("Rise, Kael.", GameConfiguration.Greet("Rise, {name}.", "Kael"));

        // A message with no token is sent as written rather than having a name bolted on.
        Assert.Equal("The gate is dark.", GameConfiguration.Greet("The gate is dark.", "Kael"));
    }
}
