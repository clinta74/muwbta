using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Server.Building;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// The builder API end to end (PLAN.md §7.3), through the real HTTP stack, the real game loop,
/// and a real PostgreSQL. Every write here goes enqueue → loop applies → persist → notify.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class BuilderApiTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    // -----------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_builder_api_is_closed_to_anonymous_callers()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_player_is_forbidden_not_merely_unauthorized()
    {
        // A logged-in player must get 403, not 401: 401 would tell the client to try logging
        // in again, which cannot possibly help.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterAsync(client);

        var read = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));
        var write = await client.PostAsJsonAsync("/api/builder/worlds/nope", new { name = "Nope" });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task A_moderator_is_not_a_builder()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Moderator);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_do_everything_a_builder_can()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);

        var username = await BuilderClient.RegisterAsync(client);
        await BuilderClient.SetRoleAsync(factory, username, AccountRole.Admin);
        await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" });

        var response = await client.GetAsync(new Uri("/api/builder/worlds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // CRUD
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_zone_can_be_built_end_to_end_with_no_sql()
    {
        // The Phase 2 acceptance criterion in miniature.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (worldKey, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.Equal(roomKey, room.GetProperty("key").GetString());
        Assert.Equal(zoneKey, room.GetProperty("zoneKey").GetString());

        var zones = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones?world={worldKey}", UriKind.Relative)));

        Assert.Equal(1, zones.GetArrayLength());
        Assert.Equal(1, zones[0].GetProperty("roomCount").GetInt32());
    }

    [Fact]
    public async Task Creating_something_that_already_exists_is_a_conflict()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var again = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{zoneKey}.gate", new { zoneKey, title = "Again" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_patch_leaves_unmentioned_fields_alone()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new { title = "The Iron Gate" });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.Equal("The Iron Gate", room.GetProperty("title").GetString());
        Assert.Equal("A room made by a test.", room.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_room_key_that_does_not_match_its_zone_is_rejected()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/rooms/elsewhere.other.room", new { zoneKey, title = "Wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_room_key_is_rejected_before_it_reaches_the_loop()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/rooms/NotAKey", new { title = "Wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Exits and dangling links (PLAN.md §7.4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_exit_may_be_linked_before_its_destination_exists()
    {
        // Live editing has no publish gate to defer the link to, so this must be allowed -
        // and the response must say plainly that the target is not there yet.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var response = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north",
            new { to = $"{zoneKey}.not-yet" });

        var room = await BuilderClient.JsonAsync(response);
        var exit = room.GetProperty("exits")[0];

        Assert.Equal("north", exit.GetProperty("direction").GetString());
        Assert.False(exit.GetProperty("targetExists").GetBoolean());
    }

    [Fact]
    public async Task Linking_two_existing_rooms_creates_the_reciprocal_by_default()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var first = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");
        var second = await BuilderClient.NewRoomAsync(client, zoneKey, "road");

        await client.PutAsJsonAsync($"/api/builder/rooms/{first}/exits/north", new { to = second });

        var back = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{second}", UriKind.Relative)));

        var exit = back.GetProperty("exits")[0];
        Assert.Equal("south", exit.GetProperty("direction").GetString());
        Assert.Equal(first, exit.GetProperty("to").GetString());
    }

    // -----------------------------------------------------------------------
    // Walk-and-build (PLAN.md §7.6)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dig_creates_a_room_and_returns_it()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));

        Assert.Equal($"{zoneKey}.room-1", dug.GetProperty("key").GetString());
        Assert.True(dug.GetProperty("flags").GetProperty("unfinished").GetBoolean());

        // And the link back exists, so walking returns you where you came from.
        Assert.Contains(
            dug.GetProperty("exits").EnumerateArray(),
            e => e.GetProperty("direction").GetString() == "south"
                && e.GetProperty("to").GetString() == roomKey);
    }

    [Fact]
    public async Task Dig_materializes_a_dangling_exit_rather_than_creating_a_second_room()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var promised = $"{zoneKey}.the-hill";
        await client.PutAsJsonAsync($"/api/builder/rooms/{roomKey}/exits/north", new { to = promised });

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));

        Assert.Equal(promised, dug.GetProperty("key").GetString());

        // The originally dangling link now resolves.
        var source = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.True(source.GetProperty("exits")[0].GetProperty("targetExists").GetBoolean());
    }

    [Fact]
    public async Task Digging_where_a_room_already_is_conflicts()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });

        // Wait out the per-account dig throttle before the second attempt, so this asserts the
        // conflict rather than accidentally asserting the rate limit.
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var again = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Dig_is_rate_limited()
    {
        // A held-down key would otherwise carve forty rooms (PLAN.md §7.6).
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var first = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "north" });
        var second = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/dig", new { direction = "east" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Unfinished_rooms_are_the_zone_build_list_and_editing_clears_them()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var dug = await BuilderClient.JsonAsync(
            await client.PostAsJsonAsync($"/api/builder/rooms/{roomKey}/dig", new { direction = "north" }));
        var dugKey = dug.GetProperty("key").GetString()!;

        var before = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/unfinished", UriKind.Relative)));

        Assert.Equal(1, before.GetArrayLength());

        await client.PatchAsJsonAsync($"/api/builder/rooms/{dugKey}", new
        {
            title = "The Hill Road",
            description = "Cart ruts run north into the hills.",
        });

        var after = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/unfinished", UriKind.Relative)));

        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task Renaming_a_room_rewrites_the_exits_that_pointed_at_it()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var gate = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");
        var road = await BuilderClient.NewRoomAsync(client, zoneKey, "road");

        await client.PutAsJsonAsync($"/api/builder/rooms/{gate}/exits/north", new { to = road });

        var renamed = $"{zoneKey}.hill-road";
        var response = await client.PostAsJsonAsync(
            $"/api/builder/rooms/{road}/rename", new { newKey = renamed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var source = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{gate}", UriKind.Relative)));

        var exit = source.GetProperty("exits")[0];
        Assert.Equal(renamed, exit.GetProperty("to").GetString());
        Assert.True(exit.GetProperty("targetExists").GetBoolean());
    }

    // -----------------------------------------------------------------------
    // Flags (PLAN.md §4.10)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_flag_registry_is_served_so_the_editor_can_render_itself()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var flags = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/room-flags", UriKind.Relative)));

        Assert.Contains(flags.EnumerateArray(), f => f.GetProperty("key").GetString() == "pvp");

        // Kind-aware, because the registry is no longer all booleans. The claim is still §4.10's:
        // whatever a flag defaults to has to be the harmless value, and for a text flag that means
        // one of its own choices rather than a word nothing recognises.
        Assert.All(flags.EnumerateArray(), flag =>
        {
            var choices = flag.GetProperty("choices").EnumerateArray()
                .Select(c => c.GetString())
                .ToList();

            if (flag.GetProperty("kind").GetString() == "text")
            {
                Assert.NotEmpty(choices);
                Assert.Contains(flag.GetProperty("default").GetString(), choices);
            }
            else
            {
                Assert.Empty(choices);
                Assert.False(flag.GetProperty("default").GetBoolean());
            }
        });

        // The one the weather reads, with its vocabulary, so the editor can draw a list.
        var climate = flags.EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == "climate");

        Assert.Equal("text", climate.GetProperty("kind").GetString());
        Assert.Equal("temperate", climate.GetProperty("default").GetString());
    }

    /// <summary>
    /// The server says which bundle format it reads, so the builder can compare a file against it
    /// before uploading rather than discovering a mismatch as a 400.
    /// </summary>
    /// <remarks>
    /// Asserted against <see cref="BundleFormat.CurrentVersion"/> rather than a literal. A test
    /// carrying its own number would be one more copy of the thing that has already drifted twice,
    /// and it would pass while telling the browser something untrue.
    /// </remarks>
    [Fact]
    public async Task The_server_reports_the_bundle_format_it_accepts()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var info = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri("/api/builder/bundle-format", UriKind.Relative)));

        Assert.Equal(BundleFormat.CurrentVersion, info.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public async Task A_zone_flag_is_inherited_by_its_rooms_and_says_so()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "arena");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        var pvp = room.GetProperty("resolved").EnumerateArray()
            .First(f => f.GetProperty("key").GetString() == "pvp");

        Assert.True(pvp.GetProperty("value").GetBoolean());
        Assert.Equal("zone", pvp.GetProperty("source").GetString());

        // The room itself declares nothing, which is what makes it inherited.
        Assert.False(room.GetProperty("flags").TryGetProperty("pvp", out _));
    }

    [Fact]
    public async Task A_room_can_override_its_zones_flag()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "chapel");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = false },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        var pvp = room.GetProperty("resolved").EnumerateArray()
            .First(f => f.GetProperty("key").GetString() == "pvp");

        Assert.False(pvp.GetProperty("value").GetBoolean());
        Assert.Equal("room", pvp.GetProperty("source").GetString());
    }

    [Fact]
    public async Task An_unrecognised_flag_key_is_dropped_rather_than_stored()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new
        {
            flags = new Dictionary<string, bool> { ["dark"] = true, ["notARealFlag"] = true },
        });

        var room = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative)));

        Assert.True(room.GetProperty("flags").GetProperty("dark").GetBoolean());
        Assert.False(room.GetProperty("flags").TryGetProperty("notARealFlag", out _));
    }

    [Fact]
    public async Task Setting_one_flag_leaves_its_siblings_alone()
    {
        // The whole point of the per-flag endpoint: two builders editing one zone must not
        // erase each other. Patching the room replaces the entire set; this must not.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var first = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/dark", new { value = true });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/indoors", new { value = true });

        var room = await BuilderClient.JsonAsync(second);

        // Both survive - the second write did not clobber the first.
        Assert.True(room.GetProperty("flags").GetProperty("dark").GetBoolean());
        Assert.True(room.GetProperty("flags").GetProperty("indoors").GetBoolean());
    }

    [Fact]
    public async Task Clearing_a_flag_removes_the_key_so_inheritance_resumes()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PutAsJsonAsync($"/api/builder/rooms/{roomKey}/flags/dark", new { value = false });

        // A null value clears the key entirely rather than storing "off".
        var room = await BuilderClient.JsonAsync(
            await client.PutAsJsonAsync(
                $"/api/builder/rooms/{roomKey}/flags/dark", new { value = (bool?)null }));

        Assert.False(room.GetProperty("flags").TryGetProperty("dark", out _));
    }

    [Fact]
    public async Task An_unknown_flag_name_is_refused_by_the_per_flag_endpoint()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var response = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/flags/notARealFlag", new { value = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Quests - authoring was impossible; WorldWriter had no arm and every save 500'd
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_quest_can_be_authored_and_read_back()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        var created = await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "The Lost Ledger",
            summary = "Find the ledger.",
            giverMobKey = "kaelen",
            turninMobKey = "captain",
            requiredItemKey = "ledger",
            requiredCount = 1,
            rewardXp = 50,
            rewardGold = 10,
            dialogue = new Dictionary<string, string> { ["giverOffer"] = "Find it?" },
        });

        // Before the writer arm existed this was a 500 with the edit rolled back.
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var quest = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/quests/{key}", UriKind.Relative)));

        Assert.Equal("The Lost Ledger", quest.GetProperty("name").GetString());
        Assert.Equal("kaelen", quest.GetProperty("giverMobKey").GetString());
        Assert.Equal("captain", quest.GetProperty("turninMobKey").GetString());
        Assert.Equal(50, quest.GetProperty("rewardXp").GetInt32());
        Assert.Equal("Find it?", quest.GetProperty("dialogue").GetProperty("giverOffer").GetString());
    }

    [Fact]
    public async Task A_quest_with_no_required_item_can_be_saved()
    {
        // required_item_key was NOT NULL although the domain property is nullable and its own
        // doc comment says a quest may have no item requirement.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        var created = await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "A Word With You",
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
            requiredItemKey = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var quest = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/quests/{key}", UriKind.Relative)));

        Assert.Equal(JsonValueKind.Null, quest.GetProperty("requiredItemKey").ValueKind);
    }

    [Fact]
    public async Task Patching_a_quest_leaves_unmentioned_fields_alone()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "The Lost Ledger",
            giverMobKey = "kaelen",
            turninMobKey = "captain",
            rewardXp = 50,
            prerequisiteQuestKeys = new[] { "aldenmoor.first-errand" },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/quests/{key}", new
        {
            name = "The Recovered Ledger",
        })).EnsureSuccessStatusCode();

        var quest = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/quests/{key}", UriKind.Relative)));

        Assert.Equal("The Recovered Ledger", quest.GetProperty("name").GetString());
        Assert.Equal(50, quest.GetProperty("rewardXp").GetInt32());
        Assert.Equal(
            "aldenmoor.first-errand",
            quest.GetProperty("prerequisiteQuestKeys")[0].GetString());
    }

    [Fact]
    public async Task A_quest_can_be_deleted()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "Temporary",
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
        })).EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync(
            new Uri($"/api/builder/quests/{key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var gone = await client.GetAsync(new Uri($"/api/builder/quests/{key}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Authoring_a_quest_writes_an_audit_row()
    {
        // CaptureAsync had no quest arm, so audits would have been blank before/after.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "Audited",
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
        })).EnsureSuccessStatusCode();

        var audit = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/audit?kind=quest&key={key}", UriKind.Relative)));

        Assert.NotEmpty(audit.EnumerateArray());
    }

    // -----------------------------------------------------------------------
    // Quest reachability - the check that used to be tautologically true
    // -----------------------------------------------------------------------

    /// <summary>Creates a quest wired to the given mob and item keys, returning its key.</summary>
    private static async Task<string> NewQuestAsync(
        HttpClient client,
        string zoneKey,
        string giver,
        string? requiredItem)
    {
        var key = BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = "Reachability",
            giverMobKey = giver,
            turninMobKey = giver,
            requiredItemKey = requiredItem,
        })).EnsureSuccessStatusCode();

        return key;
    }

    private static async Task<string[]> WarningKindsAsync(HttpClient client, string questKey)
    {
        var report = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/quests/{questKey}/reachability", UriKind.Relative)));

        return [.. report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString() ?? string.Empty)];
    }

    [Fact]
    public async Task Reachability_warns_when_nothing_produces_the_required_item()
    {
        // The old check compared a value against the branch it was already inside, so it was
        // always true and every quest came back clean - a green light on an unfinishable quest.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var mob = BuilderClient.UniqueName("m").ToLowerInvariant();
        var item = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{mob}", new { name = "A guard" }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{item}", new { name = "A ledger" }))
            .EnsureSuccessStatusCode();

        var questKey = await NewQuestAsync(client, zoneKey, mob, item);

        Assert.Contains("unreachable-required-item", await WarningKindsAsync(client, questKey));
    }

    [Fact]
    public async Task Reachability_is_satisfied_by_a_loot_table_entry()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "lair");
        var mob = BuilderClient.UniqueName("m").ToLowerInvariant();
        var item = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{item}", new { name = "A ledger" }))
            .EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{mob}", new
        {
            name = "A guard",
            loot = new[]
            {
                new Dictionary<string, object> { ["itemTemplateKey"] = item, ["chance"] = 0.5 },
            },
        })).EnsureSuccessStatusCode();

        // The mob must actually be placed, or its loot is unreachable too.
        (await client.PostAsJsonAsync("/api/builder/spawners", new
        {
            zoneKey,
            templateKey = mob,
            templateKind = "Mob",
            roomKeys = new[] { roomKey },
            targetCount = 1,
        })).EnsureSuccessStatusCode();

        var questKey = await NewQuestAsync(client, zoneKey, mob, item);

        Assert.Empty(await WarningKindsAsync(client, questKey));
    }

    [Fact]
    public async Task Reachability_warns_when_the_only_dropper_is_never_spawned()
    {
        // Loot on a mob no spawner places is loot nobody can reach - the subtler half of the
        // same bug, and invisible in the editor.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var mob = BuilderClient.UniqueName("m").ToLowerInvariant();
        var item = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{item}", new { name = "A ledger" }))
            .EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{mob}", new
        {
            name = "A guard",
            loot = new[]
            {
                new Dictionary<string, object> { ["itemTemplateKey"] = item, ["chance"] = 0.5 },
            },
        })).EnsureSuccessStatusCode();

        var questKey = await NewQuestAsync(client, zoneKey, mob, item);
        var kinds = await WarningKindsAsync(client, questKey);

        Assert.Contains("unspawned-required-item-source", kinds);
        Assert.Contains("unspawned-giver-mob", kinds);
    }

    [Fact]
    public async Task Reachability_warns_when_the_giver_mob_does_not_exist()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var questKey = await NewQuestAsync(client, zoneKey, "no-such-mob", requiredItem: null);

        Assert.Contains("missing-giver-mob", await WarningKindsAsync(client, questKey));
    }

    // -----------------------------------------------------------------------
    // Storyline graph
    // -----------------------------------------------------------------------

    private static async Task<string> NewChainedQuestAsync(
        HttpClient client,
        string zoneKey,
        string[] prerequisites,
        string? key = null)
    {
        key ??= BuilderClient.UniqueName("q").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/quests/{key}", new
        {
            zoneKey,
            name = key,
            giverMobKey = "kaelen",
            turninMobKey = "kaelen",
            prerequisiteQuestKeys = prerequisites,
        })).EnsureSuccessStatusCode();

        return key;
    }

    [Fact]
    public async Task A_cross_zone_prerequisite_is_an_edge_not_an_unreachable_quest()
    {
        // Prerequisites are plain keys and chains legitimately cross zones. Resolving them
        // within one zone dropped the edge and then blamed the dependent quest for it.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (worldKey, firstZone) = await BuilderClient.NewZoneAsync(client);
        var secondZone = $"{worldKey}.second";

        (await client.PostAsJsonAsync($"/api/builder/zones/{secondZone}", new
        {
            worldKey,
            name = "Second Zone",
        })).EnsureSuccessStatusCode();

        var opener = await NewChainedQuestAsync(client, firstZone, []);
        var sequel = await NewChainedQuestAsync(client, secondZone, [opener]);

        var graph = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/zones/{secondZone}/storyline", UriKind.Relative)));

        Assert.Empty(graph.GetProperty("unreachable").EnumerateArray());

        Assert.Contains(
            graph.GetProperty("edges").EnumerateArray(),
            e => e.GetProperty("from").GetString() == opener
                && e.GetProperty("to").GetString() == sequel);

        // The out-of-zone prerequisite is drawn, flagged so the UI can show it differently.
        Assert.Contains(
            graph.GetProperty("nodes").EnumerateArray(),
            n => n.GetProperty("key").GetString() == opener && n.GetProperty("external").GetBoolean());
    }

    [Fact]
    public async Task A_prerequisite_cycle_is_reported_without_dragging_in_healthy_quests()
    {
        // The old detector shared state across roots, so once it found one cycle every quest
        // examined afterwards was reported as cyclic too.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);

        var left = BuilderClient.UniqueName("q").ToLowerInvariant();
        var right = BuilderClient.UniqueName("q").ToLowerInvariant();

        await NewChainedQuestAsync(client, zoneKey, [], left);
        await NewChainedQuestAsync(client, zoneKey, [left], right);

        // Close the loop, and add a quest that has nothing to do with it.
        (await client.PatchAsJsonAsync($"/api/builder/quests/{left}", new
        {
            prerequisiteQuestKeys = new[] { right },
        })).EnsureSuccessStatusCode();

        var healthy = await NewChainedQuestAsync(client, zoneKey, []);

        var graph = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/zones/{zoneKey}/storyline", UriKind.Relative)));

        var cycles = graph.GetProperty("cycles").EnumerateArray()
            .Select(c => c.GetString()).ToList();

        Assert.Contains(left, cycles);
        Assert.Contains(right, cycles);
        Assert.DoesNotContain(healthy, cycles);
    }

    [Fact]
    public async Task A_prerequisite_naming_no_quest_is_reported_separately()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var orphan = await NewChainedQuestAsync(client, zoneKey, ["nothing.named-this"]);

        var graph = await BuilderClient.JsonAsync(await client.GetAsync(
            new Uri($"/api/builder/zones/{zoneKey}/storyline", UriKind.Relative)));

        Assert.Contains(
            graph.GetProperty("missingPrerequisites").EnumerateArray(),
            m => m.GetProperty("quest").GetString() == orphan
                && m.GetProperty("missing").GetString() == "nothing.named-this");
    }

    // -----------------------------------------------------------------------
    // Templates - the round-trip defects (no coverage existed before)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_item_templates_slots_survive_as_names_not_numbers()
    {
        // The response record serialised Slot as an int while the client reads a string, so a
        // slot could not survive a load-edit-save round trip - and Head, being 0, read as unset.
        // The list has the same problem per element, which is why it carries its own converter.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A plumed helm",
            slots = new[] { "Head" },
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/item-templates/{key}", UriKind.Relative)));

        var slots = template.GetProperty("slots").EnumerateArray().ToList();
        Assert.All(slots, slot => Assert.Equal(JsonValueKind.String, slot.ValueKind));
        Assert.Equal(["Head"], slots.Select(s => s.GetString()));
    }

    [Fact]
    public async Task An_either_hand_weapons_slots_come_back_in_enum_order()
    {
        // The order is the preference order - main hand first is what makes an either-hand weapon
        // reach for it - so a list that came back as authored rather than as ordered would decide
        // which hand a blade lands in by which box a builder ticked first.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A short blade",
            slots = new[] { "OffHand", "MainHand" },
            attackDelayPulses = 6,
            attackVerb = "slash",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/item-templates/{key}", UriKind.Relative)));

        Assert.Equal(
            ["MainHand", "OffHand"],
            template.GetProperty("slots").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task A_two_handed_item_that_is_not_a_main_hand_item_is_refused()
    {
        // Refused rather than normalised: "two-handed shield" is a mistake, and quietly dropping
        // half of it is how a builder ends up certain they authored something they did not.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        var refused = await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A confused shield",
            slots = new[] { "OffHand" },
            isTwoHanded = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Setting_two_handed_on_an_existing_worn_item_is_refused()
    {
        // The merged state is what has to be coherent. A PATCH carrying only `isTwoHanded` is not
        // wrong in itself; it is wrong against the slots already stored, and validating the
        // request alone would let it through.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A plumed helm",
            slots = new[] { "Head" },
        })).EnsureSuccessStatusCode();

        var refused = await client.PatchAsJsonAsync(
            $"/api/builder/item-templates/{key}", new { isTwoHanded = true });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Patching_a_mob_templates_name_leaves_its_loot_and_behavior_intact()
    {
        // The editor sent a fresh baseStats object and never sent loot/behavior, so a name-only
        // save from the panel wiped content authored elsewhere. A field-scoped PATCH must not.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("m").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A warden",
            level = 5,
            baseStats = new Dictionary<string, object> { ["health"] = 40, ["strength"] = 12 },
            loot = new[]
            {
                new Dictionary<string, object> { ["itemKey"] = "rusted-blade", ["chance"] = 0.15 },
            },
            behavior = new Dictionary<string, object> { ["aggressive"] = true },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A grizzled warden",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/mob-templates/{key}", UriKind.Relative)));

        Assert.Equal("A grizzled warden", template.GetProperty("name").GetString());
        Assert.Equal(12, template.GetProperty("baseStats").GetProperty("strength").GetInt32());
        Assert.True(template.GetProperty("behavior").GetProperty("aggressive").GetBoolean());
        Assert.Equal(
            "rusted-blade",
            template.GetProperty("loot")[0].GetProperty("itemKey").GetString());
    }

    [Fact]
    public async Task A_weapons_verb_survives_a_round_trip_as_a_string()
    {
        // The direct analogue of the slot defect above, and the reason speed and verb are
        // columns rather than baseStats keys: the item editor coerces every base stat with
        // Number(v) || 0, which would turn "slash" into 0 on load.
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A curved sabre",
            slot = "MainHand",
            attackDelayPulses = 6,
            attackVerb = "slash",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/item-templates/{key}", UriKind.Relative)));

        var verb = template.GetProperty("attackVerb");
        Assert.Equal(JsonValueKind.String, verb.ValueKind);
        Assert.Equal("slash", verb.GetString());
        Assert.Equal(6, template.GetProperty("attackDelayPulses").GetInt32());
    }

    [Fact]
    public async Task An_attack_delay_below_the_floor_is_refused()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("i").ToLowerInvariant();

        var tooFast = await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "An impossible blade",
            attackDelayPulses = 3,
        });

        Assert.Equal(HttpStatusCode.BadRequest, tooFast.StatusCode);

        // The engine would clamp it anyway, but a builder tuning a number the game ignores is
        // worse than being told no.
        (await client.PostAsJsonAsync($"/api/builder/item-templates/{key}", new
        {
            name = "A quick blade",
            attackDelayPulses = 4,
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_mob_attack_delay_below_the_floor_is_refused()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("m").ToLowerInvariant();

        var tooFast = await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A blur",
            attacks = new[] { new { verb = "bite", delayPulses = 2 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, tooFast.StatusCode);
    }

    [Fact]
    public async Task Patching_a_mob_templates_name_leaves_its_attacks_intact()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var key = BuilderClient.UniqueName("m").ToLowerInvariant();

        (await client.PostAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A wolf",
            attacks = new[]
            {
                new { verb = "bite", delayPulses = 4, damageMultiplier = (double?)1.5 },
                new { verb = "claw", delayPulses = 6, damageMultiplier = (double?)null },
            },
        })).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/builder/mob-templates/{key}", new
        {
            name = "A dire wolf",
        })).EnsureSuccessStatusCode();

        var template = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/mob-templates/{key}", UriKind.Relative)));

        var attacks = template.GetProperty("attacks");
        Assert.Equal(2, attacks.GetArrayLength());
        Assert.Equal("bite", attacks[0].GetProperty("verb").GetString());
        Assert.Equal(4, attacks[0].GetProperty("delayPulses").GetInt32());
        Assert.Equal(1.5, attacks[0].GetProperty("damageMultiplier").GetDouble());
        Assert.Equal("claw", attacks[1].GetProperty("verb").GetString());
    }

    // -----------------------------------------------------------------------
    // Validation (PLAN.md §7.4) - advisory, never blocking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Validation_reports_dangling_exits_without_having_blocked_the_save()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        var saved = await client.PutAsJsonAsync(
            $"/api/builder/rooms/{roomKey}/exits/north", new { to = $"{zoneKey}.not-yet" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var report = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        Assert.Contains(
            report.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("kind").GetString() == "dangling-exit");
    }

    [Fact]
    public async Task Validation_names_the_rooms_that_became_pvp_by_inheritance()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "square");

        await client.PatchAsJsonAsync($"/api/builder/zones/{zoneKey}", new
        {
            flags = new Dictionary<string, bool> { ["pvp"] = true },
        });

        var report = await BuilderClient.JsonAsync(
            await client.GetAsync(new Uri($"/api/builder/zones/{zoneKey}/validate", UriKind.Relative)));

        Assert.Contains(
            report.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("kind").GetString() == "inherited-pvp"
                && w.GetProperty("entityKey").GetString() == roomKey);
    }

    // -----------------------------------------------------------------------
    // Audit (PLAN.md §6, §7.3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Every_write_leaves_an_audit_row_with_before_and_after()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "gate");

        await client.PatchAsJsonAsync($"/api/builder/rooms/{roomKey}", new { title = "The Iron Gate" });

        var audit = await BuilderClient.JsonAsync(
            await client.GetAsync(
                new Uri($"/api/builder/audit?kind=room&key={roomKey}", UriKind.Relative)));

        var actions = audit.EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .ToList();

        Assert.Contains("Create", actions);
        Assert.Contains("Update", actions);

        // And it records who, which is the question actually asked after a bad live edit.
        Assert.All(
            audit.EnumerateArray(),
            a => Assert.False(string.IsNullOrEmpty(a.GetProperty("username").GetString())));
    }

    [Fact]
    public async Task Deleting_leaves_the_audit_row_behind()
    {
        var factory = postgres.App;
        using var client = NewClient(factory);
        await BuilderClient.RegisterBuilderAsync(factory, client);

        var (_, zoneKey) = await BuilderClient.NewZoneAsync(client);
        var roomKey = await BuilderClient.NewRoomAsync(client, zoneKey, "doomed");

        var deleted = await client.DeleteAsync(new Uri($"/api/builder/rooms/{roomKey}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var audit = await BuilderClient.JsonAsync(
            await client.GetAsync(
                new Uri($"/api/builder/audit?kind=room&key={roomKey}", UriKind.Relative)));

        Assert.Contains(
            audit.EnumerateArray(),
            a => a.GetProperty("action").GetString() == "Delete");
    }
}
