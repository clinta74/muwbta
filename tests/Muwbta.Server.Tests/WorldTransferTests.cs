using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence;
using Muwbta.Server.Building;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// World export and import (PLAN.md §6, Phase 6) - the JSON that moves authored content between
/// environments, through the real HTTP stack, the real game loop, and a real PostgreSQL.
/// </summary>
/// <remarks>
/// Every test scopes its export to its own zone. The suite shares one database, so an "all"
/// export would pick up whatever else the run had authored - and a test whose subject is
/// "everything" cannot assert anything about the size of what it got back.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class WorldTransferTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    // -----------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Transferring_the_world_is_closed_to_ordinary_players()
    {
        // Asserted on these two routes specifically rather than trusted to the group: a route
        // mapped a line outside the MapGroup would be an unauthenticated dump of the whole world,
        // and nothing else in the suite would notice.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var export = await client.GetAsync(new Uri("/api/builder/export", UriKind.Relative));
        var import = await client.PostAsJsonAsync("/api/builder/import", new { formatVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
    }

    [Fact]
    public async Task Exporting_a_zone_that_does_not_exist_is_not_an_empty_bundle()
    {
        // An empty bundle would be indistinguishable from a correct export of an empty zone,
        // which is exactly the answer somebody restores from and then wonders where it went.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.GetAsync(
            new Uri("/api/builder/export?zone=nosuch.zone", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // What a scoped bundle carries
    // -----------------------------------------------------------------------

    /// <summary>
    /// A configuration's bundle is its worlds and itself: not the other configuration on the
    /// server, not the world that is none of its business, and with its canon on board.
    /// </summary>
    [Fact]
    public async Task A_configuration_bundle_carries_its_worlds_and_itself()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (mine, myZone) = await BuilderClient.NewZoneAsync(client);
        var (theirs, theirZone) = await BuilderClient.NewZoneAsync(client);

        var key = $"whole-{Guid.NewGuid():N}"[..24];
        (await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            new { name = "Whole", startingRoomKey = $"{myZone}.start", worldKeys = new[] { mine }, canon = "# Mine\n" }))
            .EnsureSuccessStatusCode();

        var bundle = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/export?configuration={key}", UriKind.Relative)));

        Assert.Equal("configuration", bundle.GetProperty("scope").GetProperty("kind").GetString());
        Assert.Equal([mine], bundle.GetProperty("worlds").EnumerateArray().Select(w => w.GetProperty("key").GetString()));

        var zones = bundle.GetProperty("zones").EnumerateArray().Select(z => z.GetProperty("key").GetString()).ToList();
        Assert.Contains(myZone, zones);
        Assert.DoesNotContain(theirZone, zones);
        Assert.NotEqual(mine, theirs);

        var configuration = Assert.Single(bundle.GetProperty("configurations").EnumerateArray());
        Assert.Equal(key, configuration.GetProperty("key").GetString());
        Assert.Equal("# Mine\n", configuration.GetProperty("canon").GetString());
        Assert.Equal([mine], configuration.GetProperty("worldKeys").EnumerateArray().Select(w => w.GetString()));

        var missing = await client.GetAsync(new Uri("/api/builder/export?configuration=nosuch", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    /// A configuration with no canon exports an empty one: the server embeds nothing to fall back
    /// on, and the bundle says exactly what the assist is told.
    /// </summary>
    [Fact]
    public async Task A_configuration_with_no_canon_exports_an_empty_one()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);
        var (mine, myZone) = await BuilderClient.NewZoneAsync(client);

        var key = $"plain-{Guid.NewGuid():N}"[..24];
        (await client.PostAsJsonAsync(
            $"/api/builder/configurations/{key}",
            new { name = "Plain", startingRoomKey = $"{myZone}.start", worldKeys = new[] { mine } }))
            .EnsureSuccessStatusCode();

        var bundle = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/export?configuration={key}", UriKind.Relative)));

        var configuration = Assert.Single(bundle.GetProperty("configurations").EnumerateArray());
        Assert.Equal(string.Empty, configuration.GetProperty("canon").GetString());
    }

    [Fact]
    public async Task A_zone_bundle_carries_the_templates_its_content_needs()
    {
        // The load-bearing property of a scoped export. Rooms, spawners, and quests belong to a
        // zone, so scoping those is a filter; templates are global, so a bundle that filtered
        // them the same way would import cleanly and then spawn nothing.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var bundle = await ExportZoneAsync(client, content.ZoneKey);

        Assert.Contains(content.MobKey, KeysOf(bundle, "mobTemplates"));

        // Reached through the spawner, which places the mob.
        Assert.Contains(content.QuestItemKey, KeysOf(bundle, "itemTemplates"));

        // Reached only through the mob's loot table - no spawner places it and no quest names it
        // directly, so a closure that stopped at spawners and quests would leave the quest
        // unfinishable in the target environment (§10).
        Assert.Contains(content.LootKey, KeysOf(bundle, "itemTemplates"));
    }

    [Fact]
    public async Task A_zone_bundle_carries_the_world_above_it()
    {
        // Multipliers resolve through the world (§4.4), so a zone imported without its world
        // would produce wrong numbers rather than merely missing ones.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var bundle = await ExportZoneAsync(client, content.ZoneKey);

        Assert.Equal([content.WorldKey], KeysOf(bundle, "worlds"));
        Assert.Equal([content.ZoneKey], KeysOf(bundle, "zones"));
        Assert.Equal("zone", bundle.GetProperty("scope").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_zone_bundle_does_not_carry_another_zones_rooms()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var mine = await AuthorZoneAsync(client);
        var theirs = await AuthorZoneAsync(client);

        var bundle = await ExportZoneAsync(client, mine.ZoneKey);
        var rooms = KeysOf(bundle, "rooms");

        Assert.Contains(mine.RoomKey, rooms);
        Assert.DoesNotContain(theirs.RoomKey, rooms);
    }

    // -----------------------------------------------------------------------
    // The round trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_import_restores_what_was_edited_after_the_export()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PatchAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}",
            new { title = "Edited after the export" })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        Assert.Equal(content.RoomTitle, room.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_import_restores_the_exits_that_were_removed_after_the_export()
    {
        // Exits travel inside their room rather than as their own list, so this is the assertion
        // that they travel at all - a bundle that carried rooms and dropped the graph between
        // them would look complete and import a zone nobody can walk through.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.DeleteAsync(
            new Uri($"/api/builder/rooms/{content.RoomKey}/exits/north", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        await ImportAsync(client, json);

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        var directions = room.GetProperty("exits").EnumerateArray()
            .Select(e => e.GetProperty("direction").GetString())
            .ToList();

        Assert.Contains("north", directions);
    }

    [Fact]
    public async Task Importing_the_same_bundle_twice_does_not_double_the_population()
    {
        // A spawner has no content key to collide on, so it travels with its id. Minting a fresh
        // one per import would double every zone's population on the second run - and a doubled
        // spawner is invisible in the editor and obvious only in play.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        await ImportAsync(client, json);
        await ImportAsync(client, json);

        var spawners = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/spawners?zone={content.ZoneKey}", UriKind.Relative)));

        Assert.Equal(1, spawners.GetArrayLength());
    }

    [Fact]
    public async Task A_pinned_level_survives_a_round_trip()
    {
        // The field is on the spawner rather than the template, so it travels only if every one of
        // the entity, the change, the writer, the bundle, the exporter and the importer carries it
        // - six places, and a bundle that quietly dropped it would land content that plays
        // differently in the environment it was moved to.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        var existing = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/spawners?zone={content.ZoneKey}", UriKind.Relative)));
        var id = existing[0].GetProperty("id").GetString();

        (await client.PatchAsJsonAsync($"/api/builder/spawners/{id}", new { level = "27" }))
            .EnsureSuccessStatusCode();

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        Assert.Contains("\"fightsAtLevel\":27", json, StringComparison.Ordinal);

        await ImportAsync(client, json);

        var after = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/spawners/{id}", UriKind.Relative)));

        Assert.Equal("27", after.GetProperty("level").GetString());
        Assert.Equal(27, after.GetProperty("fightsAtLevel").GetInt32());
    }

    /// <summary>
    /// Everything an item template carries besides its numbers: lore, no-drop, Path, and light.
    /// </summary>
    /// <remarks>
    /// <b>Written for the light source and it caught the other three.</b> None of the four were
    /// ever written to the database — the API accepted them, the applier put them in the running
    /// cache, the exporter wrote them and the importer read them, and <c>WorldWriter</c> set
    /// neither on create nor on update. So every restriction authored in the builder or landed by
    /// an import survived exactly as long as the process did, and twenty items in <c>content/</c>
    /// carry one.
    ///
    /// The four are asserted together deliberately: they fail as a group, they were fixed as a
    /// group, and a test per field would be four database round trips to learn the same thing.
    /// </remarks>
    [Fact]
    public async Task An_item_templates_flags_survive_a_round_trip()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        (await client.PatchAsJsonAsync(
            $"/api/builder/item-templates/{content.LootKey}",
            new
            {
                isLightSource = true,
                isLore = true,
                isNoDrop = true,
                paths = new[] { "Warden" },
            })).EnsureSuccessStatusCode();

        // Read back before exporting. The bug this covers put the flags in the cache and not the
        // row, and the exporter reads the database — so a GET that agreed and an export that did
        // not is precisely the shape being pinned.
        var saved = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/item-templates/{content.LootKey}", UriKind.Relative)));
        Assert.True(saved.GetProperty("isLightSource").GetBoolean());

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        Assert.Contains("\"isLightSource\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isLore\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isNoDrop\":true", json, StringComparison.Ordinal);

        await ImportAsync(client, json);

        var after = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/item-templates/{content.LootKey}", UriKind.Relative)));

        Assert.True(after.GetProperty("isLightSource").GetBoolean());
        Assert.True(after.GetProperty("isLore").GetBoolean());
        Assert.True(after.GetProperty("isNoDrop").GetBoolean());
        Assert.Equal(
            ["Warden"],
            after.GetProperty("paths").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task A_gated_exit_survives_a_round_trip()
    {
        // The same six-places argument as the pinned level above, and with a worse failure: a
        // bundle that dropped the conditions would land a world whose gates are all open, and
        // nothing about the imported zone would look wrong until a level 4 walked into the last
        // realm (PLAN.md §4.15).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/north",
            new
            {
                to = content.NorthRoomKey,
                requiredFlagKey = "attuned.grask",
                requiredItemKey = "brass-key",
                refusalMessage = "The gate does not know you.",
            }))
            .EnsureSuccessStatusCode();

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        Assert.Contains("\"requiredFlagKey\":\"attuned.grask\"", json, StringComparison.Ordinal);

        // Take the lock off, then let the import put it back - which is what proves the import
        // writes the fields rather than merely leaving them where they were.
        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/north",
            new { to = content.NorthRoomKey }))
            .EnsureSuccessStatusCode();

        await ImportAsync(client, json);

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        var north = room.GetProperty("exits").EnumerateArray()
            .Single(e => e.GetProperty("direction").GetString() == "north");

        Assert.Equal("attuned.grask", north.GetProperty("requiredFlagKey").GetString());
        Assert.Equal("brass-key", north.GetProperty("requiredItemKey").GetString());
        Assert.Equal("The gate does not know you.", north.GetProperty("refusalMessage").GetString());
    }

    [Fact]
    public async Task A_second_import_of_an_unchanged_bundle_updates_and_creates_nothing()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        var report = await ImportAsync(client, json);

        foreach (var count in report.GetProperty("counts").EnumerateArray())
        {
            Assert.Equal(0, count.GetProperty("created").GetInt32());
        }
    }

    [Fact]
    public async Task A_flag_this_build_does_not_recognise_survives_the_round_trip()
    {
        // §4.10 in transit. The builder API drops an unknown flag on the way in, deliberately -
        // but an export/import is transport, not authoring, and silently rewriting content on its
        // way between two environments running different builds is the failure this guards.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        const string Unknown = "experimental-fog";

        await SetFlagDirectlyAsync(factory, content.RoomKey, flags =>
        {
            var next = flags.Clone();
            next.Set(Unknown, true);
            return next;
        });

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        Assert.Contains(Unknown, json, StringComparison.Ordinal);

        await SetFlagDirectlyAsync(factory, content.RoomKey, flags =>
        {
            var next = flags.Clone();
            next.Clear(Unknown);
            return next;
        });

        await ImportAsync(client, json);

        Assert.True(await HasFlagDirectlyAsync(factory, content.RoomKey, Unknown));
    }

    // -----------------------------------------------------------------------
    // Rehearsing, and refusing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_dry_run_reports_what_it_would_do_and_changes_nothing()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PatchAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}",
            new { title = "Edited after the export" })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json, dryRun: true);

        Assert.True(report.GetProperty("dryRun").GetBoolean());

        // Both of the zone's rooms are already here, so both count as updates and neither as a
        // creation - which is the distinction a builder reads the rehearsal for.
        var rooms = report.GetProperty("counts").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == "room");

        Assert.Equal(2, rooms.GetProperty("updated").GetInt32());
        Assert.Equal(0, rooms.GetProperty("created").GetInt32());

        // The rehearsal is worth nothing if it also performs.
        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        Assert.Equal("Edited after the export", room.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_bundle_from_another_format_version_is_refused_outright()
    {
        // The one hard refusal in the import path. A bundle this build cannot read would
        // otherwise apply the fields that happened to match and silently drop the rest.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        // Rewritten off the current version rather than off the literal 1. Written as a literal
        // this silently stopped testing anything the day the format went to 2: the replace matched
        // nothing, the bundle stayed valid, and the assertion failed rather than passing wrongly -
        // which is only luck, since a test asserting a refusal would have gone green if the import
        // had happened to reject it for some other reason.
        var json = (await ExportZoneJsonAsync(client, content.ZoneKey))
            .Replace(
                $"\"formatVersion\":{WorldBundle.CurrentFormatVersion}",
                "\"formatVersion\":99",
                StringComparison.Ordinal);

        Assert.Contains("\"formatVersion\":99", json, StringComparison.Ordinal);

        var response = await client.PostAsync(
            new Uri("/api/builder/import", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_bundle_from_the_previous_format_version_is_refused()
    {
        // Written against the version immediately behind the current one, so this keeps testing
        // the real boundary as the format moves rather than an increasingly historical number.
        //
        // Most bumps so far have been cases where the older file cannot say what the newer one is
        // asked to. v1 -> v2: a spawner's `sentinel: bool` became `wanders: bool?`, so a v1 bundle
        // read as v2 deserialises the missing key to null and every spawner in it changes
        // behaviour without a word. v2 -> v3: abilities travel now, and a v2 bundle carries none,
        // so reading one would import an empty ability list. v3 -> v4: an ability carries a list of
        // effects rather than one, so a v3 ability would arrive doing nothing at all.
        //
        // v4 -> v5 is the weaker kind, and worth naming as such: a v4 bundle has no
        // `fightsAtLevel`, which reads correctly as "the zone decides" - exactly what a v4 spawner
        // meant. It is refused anyway because the exporter now writes a key a v4 reader would drop,
        // and a file labelled 4 carrying a v5 field is a lie about its own shape.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = (await ExportZoneJsonAsync(client, content.ZoneKey))
            .Replace(
                $"\"formatVersion\":{WorldBundle.CurrentFormatVersion}",
                $"\"formatVersion\":{WorldBundle.CurrentFormatVersion - 1}",
                StringComparison.Ordinal);

        var response = await client.PostAsync(
            new Uri("/api/builder/import", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Abilities
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_bundle_carries_abilities_whatever_the_scope()
    {
        // An ability belongs to a Path, not to a zone, so there is nothing to scope it by - and a
        // zone bundle that carried none would move a crypt into an environment where the abilities
        // meant to fight through it are whatever that server happened to have.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var bundle = await ExportZoneAsync(client, content.ZoneKey);

        var keys = KeysOf(bundle, "abilities");

        // The seeded examples, one per Path. The game's set arrives by import like everything
        // else, so what this asserts is that abilities travel at all - not that any particular
        // one of them is in the database this test built.
        Assert.Contains("warden.kick", keys);
        Assert.Contains("hallow.mend", keys);
    }

    // -----------------------------------------------------------------------
    // Abilities on their own
    // -----------------------------------------------------------------------

    /// <summary>
    /// The abilities and nothing else, which is the return leg of tuning one.
    /// </summary>
    /// <remarks>
    /// <b>Asked for while a retune sat in a database and nowhere else.</b> Abilities are content —
    /// they live in <c>shipped/abilities.json</c> and a fresh install seeds from the file — so a
    /// change made in the editor has to be able to get back to it. Every other export carries the
    /// whole world, and hand-deleting nine collections out of the JSON is a step nobody does twice.
    /// </remarks>
    [Fact]
    public async Task An_abilities_export_carries_abilities_and_nothing_else()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        // Authored first, so the emptiness below is a filter doing its job rather than a database
        // that had nothing in it to leave out.
        var content = await AuthorZoneAsync(client);

        var bundle = await ExportAbilitiesAsync(client);

        Assert.NotEmpty(KeysOf(bundle, "abilities"));
        Assert.DoesNotContain(content.RoomKey, KeysOf(bundle, "rooms"));

        foreach (var empty in new[]
        {
            "worlds", "zones", "rooms", "itemTemplates", "mobTemplates",
            "spawners", "quests", "configurations",
        })
        {
            Assert.True(
                bundle.GetProperty(empty).GetArrayLength() == 0,
                $"An abilities export should carry no {empty}.");
        }

        Assert.Equal("abilities", bundle.GetProperty("scope").GetProperty("kind").GetString());
    }

    /// <summary>And it is a bundle like any other, so it imports.</summary>
    /// <remarks>
    /// The property that makes the file useful rather than merely readable. A bundle whose other
    /// nine collections are empty must merge as "change these abilities, leave everything else" —
    /// an import is a merge and absence is not deletion (§6.1) — or saving one over
    /// <c>shipped/abilities.json</c> would quietly empty a world on the next import.
    /// </remarks>
    [Fact]
    public async Task An_abilities_export_imports_without_touching_anything_else()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportAbilitiesJsonAsync(client);

        (await client.PatchAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}",
            new { title = "Edited after the export" })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{content.RoomKey}", UriKind.Relative)));

        // Untouched, because the file said nothing about it.
        Assert.Equal("Edited after the export", room.GetProperty("title").GetString());
    }

    /// <summary>A retune goes out and comes back, which is the whole point of the file.</summary>
    [Fact]
    public async Task A_retune_survives_an_abilities_only_round_trip()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownPulses = 88 })).EnsureSuccessStatusCode();

        var json = await ExportAbilitiesJsonAsync(client);

        // Put back to something else, so the import restores rather than agrees.
        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownPulses = 24 })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var after = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities/warden.kick", UriKind.Relative)));

        Assert.Equal(88, after.GetProperty("cooldownPulses").GetInt64());

        // Left as it was found, since the suite shares one database.
        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownPulses = 24 })).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A name this cannot export on its own is refused, rather than quietly exporting the world.
    /// </summary>
    /// <remarks>
    /// The failure worth refusing: <c>?only=quests</c> returning a full world bundle would be
    /// saved over a quest file and take five worlds with it.
    /// </remarks>
    [Fact]
    public async Task An_unknown_only_is_refused_rather_than_ignored()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.GetAsync(
            new Uri("/api/builder/export?only=quests", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_import_restores_a_retuned_cooldown()
    {
        // The delivery path for a retune, end to end: export, change the number here, import, and
        // the exported value is what comes back. This is what replaced the old arrangement where
        // the startup reconcile pushed catalogue values over whatever was stored.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        var before = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities/warden.kick", UriKind.Relative)));
        var exported = before.GetProperty("cooldownPulses").GetInt64();

        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownPulses = exported + 40 })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var after = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities/warden.kick", UriKind.Relative)));

        Assert.Equal(exported, after.GetProperty("cooldownPulses").GetInt64());
    }

    [Fact]
    public async Task A_shared_timer_survives_a_round_trip()
    {
        // The delivery path for a grouping, end to end. Worth its own test because the field is
        // nullable and a v12 bundle simply has no such key: a reader that dropped it would produce
        // a Warden whose four walls no longer share a timer, with nothing on screen to say so -
        // which is exactly the silent direction the format version bump exists to refuse.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownGroup = 9 })).EnsureSuccessStatusCode();

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        // Taken off again, so the import has something to restore rather than something to agree
        // with - an assertion against a value that never moved would pass on a dropped field.
        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownGroup = (int?)null })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);
        Assert.True(report.GetProperty("failures").GetArrayLength() == 0, Raw(report));

        var after = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/abilities/warden.kick", UriKind.Relative)));

        Assert.Equal(9, after.GetProperty("cooldownGroup").GetInt32());

        // Left as it was found, since the suite shares one database.
        (await client.PatchAsJsonAsync(
            "/api/builder/abilities/warden.kick",
            new { cooldownGroup = (int?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_ability_authored_here_survives_an_import()
    {
        // An import is a merge, not a mirror (§6.1). A bundle that does not mention an ability
        // must not be read as "this environment should not have it" - the same rule that keeps a
        // zone import from deleting another zone.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        var key = $"temper.local{Guid.NewGuid():N}"[..22];
        (await client.PostAsJsonAsync($"/api/builder/abilities/{key}", new
        {
            path = "Temper",
            unlockLevel = 4,
            name = "Local Only",
            description = "Authored after the export.",
            costType = "Stamina",
            costValue = 9,
            cooldownPulses = 16,
            targetingType = "SingleTarget",
            effects = new[]
            {
                new
                {
                    key = "damage.physical",
                    @params = new Dictionary<string, string> { ["scalingFactor"] = "1.1" },
                },
            },
        })).EnsureSuccessStatusCode();

        await ImportAsync(client, json);

        var still = await client.GetAsync(new Uri($"/api/builder/abilities/{key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task A_reference_the_bundle_does_not_carry_is_a_warning_not_a_refusal()
    {
        // Advisory, exactly as /validate is (§7.4). Importing one zone of several legitimately
        // leaves exits pointing at rooms that are not here yet, and refusing that would make the
        // zone-at-a-time workflow the scoped export exists for impossible.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/south",
            new { to = $"{content.ZoneKey}.elsewhere", reciprocal = false }))
            .EnsureSuccessStatusCode();

        var json = await ExportZoneJsonAsync(client, content.ZoneKey);
        var report = await ImportAsync(client, json);

        var warnings = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString())
            .ToList();

        Assert.Contains("dangling-exit", warnings);
        Assert.Equal(0, report.GetProperty("failures").GetArrayLength());
    }

    [Fact]
    public async Task An_import_is_answerable_afterwards()
    {
        // Every entity goes through WorldEditor, so an import writes a content_audit row per
        // entity the same way a hand edit does. For a change this size that is the whole point -
        // "who replaced the crypt" has to be one query (§10).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        await ImportAsync(client, json);

        var audit = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/audit?kind=room&key={content.RoomKey}", UriKind.Relative)));

        Assert.True(audit.GetArrayLength() >= 2, "The import should have left an audit row of its own.");
    }

    // -----------------------------------------------------------------------
    // Exits the bundle does not ask for (WorldImporter remarks)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_exit_re_authored_as_a_different_direction_does_not_leave_its_old_self_behind()
    {
        // The case that found this. An exit is keyed by room and direction, so moving one from
        // north to up writes a new key and says nothing about the old one - and the report read
        // "1 created" and was telling the truth. Eight crossings between the Reaches were moved to
        // `up` that way and every one kept its lateral twin, so the Reaches were still walkable
        // sideways and the import had reported complete success twice.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        await ImportAsync(client, Redirected(json, content.RoomKey, from: "north", to: "up"));

        var directions = await DirectionsAsync(client, content.RoomKey);

        Assert.Contains("up", directions);
        Assert.DoesNotContain("north", directions);
    }

    [Fact]
    public async Task An_exit_dug_by_hand_out_of_a_room_the_bundle_owns_does_not_survive_the_next_import()
    {
        // The price of the rule above, pinned rather than left to be discovered. A room in the
        // bundle states its complete exit set, so anything else in that room is something the
        // bundle has decided against. Reciprocal is off so the assertion is about one exit.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/east",
            new { to = content.NorthRoomKey, reciprocal = false })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json);

        Assert.DoesNotContain("east", await DirectionsAsync(client, content.RoomKey));
        Assert.Equal(1, Count(report, "exit").GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task An_exit_in_a_room_the_bundle_has_never_heard_of_is_left_alone()
    {
        // The safety boundary, and the reason this is not a general replace mode. The prune reads
        // only rooms the bundle carries; a zone somebody else authored is not consulted, is not
        // compared, and cannot be pruned by importing something unrelated over the top of it.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var mine = await AuthorZoneAsync(client);
        var theirs = await AuthorZoneAsync(client);

        var json = await ExportZoneJsonAsync(client, mine.ZoneKey);
        var report = await ImportAsync(client, json);

        Assert.Contains("north", await DirectionsAsync(client, theirs.RoomKey));
        Assert.Equal(0, Count(report, "exit").GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task A_dry_run_names_every_exit_it_would_remove_and_removes_none_of_them()
    {
        // A deletion is the one thing in an import that re-importing cannot undo, so the rehearsal
        // has to work out the same removals the real run does - from the same code, which is why
        // the bundle's wanted set is collected before the dry-run check rather than after it.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var content = await AuthorZoneAsync(client);
        var json = await ExportZoneJsonAsync(client, content.ZoneKey);

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{content.RoomKey}/exits/east",
            new { to = content.NorthRoomKey, reciprocal = false })).EnsureSuccessStatusCode();

        var report = await ImportAsync(client, json, dryRun: true);

        Assert.Equal(1, Count(report, "exit").GetProperty("removed").GetInt32());

        var named = report.GetProperty("warnings").EnumerateArray()
            .Where(w => w.GetProperty("kind").GetString() == "stale-exit")
            .Select(w => (Key: w.GetProperty("entityKey").GetString(), Message: w.GetProperty("message").GetString()))
            .ToList();

        var mine = Assert.Single(named, w => w.Key == content.RoomKey);
        Assert.Contains("east", mine.Message);

        // Said in the conditional, because a rehearsal that reports a removal in the past tense is
        // reporting something that has not happened.
        Assert.Contains("would be removed", mine.Message);

        Assert.Contains("east", await DirectionsAsync(client, content.RoomKey));
    }

    // -----------------------------------------------------------------------
    // Authoring a zone with one of everything
    // -----------------------------------------------------------------------

    private sealed record AuthoredZone(
        string WorldKey,
        string ZoneKey,
        string RoomKey,
        string NorthRoomKey,
        string RoomTitle,
        string MobKey,
        string QuestItemKey,
        string LootKey,
        string QuestKey);

    /// <summary>
    /// A zone with one of every content type, wired so that each reference the exporter has to
    /// close over is exercised exactly once: a spawner reaches the mob, the quest reaches the
    /// item it requires, and the mob's loot table reaches an item nothing else names.
    /// </summary>
    private static async Task<AuthoredZone> AuthorZoneAsync(HttpClient client)
    {
        var (worldKey, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "start");
        var northKey = await BuilderClient.NewRoomAsync(client, zoneKey, "north");

        (await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north",
            new { to = northKey, reciprocal = true })).EnsureSuccessStatusCode();

        var lootKey = BuilderClient.UniqueName("loot").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{lootKey}", new
        {
            name = "a tarnished fang",
            description = "Dropped, never placed.",
        })).EnsureSuccessStatusCode();

        var questItemKey = BuilderClient.UniqueName("token").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{questItemKey}", new
        {
            name = "a carved token",
            description = "What the quest asks for.",
            isQuestItem = true,
        })).EnsureSuccessStatusCode();

        var mobKey = BuilderClient.UniqueName("mob").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{mobKey}", new
        {
            name = "a hollow warden",
            description = "Stands here.",
            level = 3,
            loot = new[]
            {
                new Dictionary<string, object>
                {
                    ["itemTemplateKey"] = lootKey,
                    ["chance"] = 0.5,
                },
            },
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = mobKey,
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = 1,
        })).EnsureSuccessStatusCode();

        var questKey = BuilderClient.UniqueName("quest").ToLowerInvariant();
        (await client.PostAsJsonAsync($"/api/builder/quests/{questKey}", new
        {
            zoneKey,
            name = "The carved token",
            giverMobKey = mobKey,
            turninMobKey = mobKey,
            requiredItemKey = questItemKey,
            requiredCount = 1,
            rewardXp = 40,
        })).EnsureSuccessStatusCode();

        return new AuthoredZone(
            worldKey, zoneKey, roomKey, northKey, "The start room", mobKey, questItemKey, lootKey,
            questKey);
    }

    /// <summary>
    /// A scoped export carries the configurations for the worlds it exports, and no others.
    /// </summary>
    /// <remarks>
    /// It used to carry every configuration on the server, whatever the scope, on the reasoning
    /// that a configuration belongs to a server rather than to a zone. True, and it meant a realm's
    /// content file shipped the starter configuration of whichever machine exported it - so
    /// importing that file elsewhere planted a configuration whose starting room the receiving
    /// server did not have, pointing into a world it may never have heard of. The Reaches' six
    /// files each carried `aldenmoor-starter` for exactly this reason.
    /// </remarks>
    [Fact]
    public async Task A_scoped_export_leaves_another_world_s_configuration_behind()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (worldKey, zoneKey) = await BuilderClient.NewZoneAsync(client);
        await BuilderClient.NewRoomAsync(client, zoneKey, "start");

        var key = $"cfg-{Guid.NewGuid():N}"[..20];

        // A configuration for a world this export does not carry.
        var created = await client.PostAsJsonAsync($"/api/builder/configurations/{key}", new
        {
            name = "Somewhere else",
            description = "Authored by a test.",
            startingRoomKey = "aldenmoor.millbrook.north-gate",
            welcomeMessage = "Welcome, {name}.",
            worldKeys = new[] { "aldenmoor" },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var bundle = await ExportZoneAsync(client, zoneKey);
        var carried = bundle.GetProperty("configurations").EnumerateArray()
            .Select(c => c.GetProperty("key").GetString())
            .ToList();

        Assert.DoesNotContain(key, carried);
        Assert.All(
            bundle.GetProperty("configurations").EnumerateArray(),
            c => Assert.Contains(
                worldKey,
                c.GetProperty("worldKeys").EnumerateArray().Select(w => w.GetString())));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<string> ExportZoneJsonAsync(HttpClient client, string zoneKey)
    {
        var response = await client.GetAsync(
            new Uri($"/api/builder/export?zone={zoneKey}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ExportZoneAsync(HttpClient client, string zoneKey) =>
        JsonDocument.Parse(await ExportZoneJsonAsync(client, zoneKey)).RootElement;

    private static async Task<string> ExportAbilitiesJsonAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/builder/export?only=abilities", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ExportAbilitiesAsync(HttpClient client) =>
        JsonDocument.Parse(await ExportAbilitiesJsonAsync(client)).RootElement;

    private static async Task<JsonElement> ImportAsync(
        HttpClient client,
        string bundleJson,
        bool dryRun = false)
    {
        var response = await client.PostAsync(
            new Uri($"/api/builder/import?dryRun={dryRun}", UriKind.Relative),
            new StringContent(bundleJson, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        // 207 is a partial import, which the assertions read rather than throw on - the report
        // names what failed, and that is more useful than an exception saying only that it did.
        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MultiStatus,
            $"Import returned {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>The directions leaving a room, as the builder API reports them.</summary>
    private static async Task<IReadOnlyList<string>> DirectionsAsync(HttpClient client, string roomKey)
    {
        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        return
        [
            .. room.GetProperty("exits").EnumerateArray()
                .Select(e => e.GetProperty("direction").GetString()!),
        ];
    }

    private static JsonElement Count(JsonElement report, string kind) =>
        report.GetProperty("counts").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == kind);

    /// <summary>
    /// Re-authors one exit's direction in the bundle, which is what a content edit looks like by
    /// the time it reaches the importer. Done to the JSON rather than through the builder API on
    /// purpose: the point is a bundle that disagrees with this environment, and editing the
    /// environment first would remove the disagreement.
    /// </summary>
    private static string Redirected(string bundleJson, string roomKey, string from, string to)
    {
        var bundle = JsonNode.Parse(bundleJson)!;

        var room = bundle["rooms"]!.AsArray()
            .Single(r => r!["key"]!.GetValue<string>() == roomKey)!;

        var exit = room["exits"]!.AsArray()
            .Single(e => string.Equals(
                e!["direction"]!.GetValue<string>(), from, StringComparison.OrdinalIgnoreCase))!;

        exit["direction"] = to;
        return bundle.ToJsonString();
    }

    private static IReadOnlyList<string> KeysOf(JsonElement bundle, string collection) =>
    [
        .. bundle.GetProperty(collection).EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()!),
    ];

    private static string Raw(JsonElement element) => element.GetRawText();

    /// <summary>
    /// Writes a flag straight to the row, because the builder API refuses to create an unknown
    /// one - which is the situation being simulated: a flag another build wrote.
    /// </summary>
    private static async Task SetFlagDirectlyAsync(
        WebApplicationFactory<Program> factory,
        string roomKey,
        Func<FlagSet, FlagSet> edit)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var key = RoomKey.Parse(roomKey);
        var room = await db.Rooms.FirstAsync(r => r.Key == key);

        // A new instance rather than a mutation in place: the column is a converted value, so a
        // change EF cannot see by reference would not be written.
        room.Flags = edit(room.Flags);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> HasFlagDirectlyAsync(
        WebApplicationFactory<Program> factory,
        string roomKey,
        string flag)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var key = RoomKey.Parse(roomKey);
        var room = await db.Rooms.AsNoTracking().FirstAsync(r => r.Key == key);

        return room.Flags.Has(flag);
    }
}
