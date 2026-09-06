using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Muwbta.Server.Building;
using Muwbta.Server.Game;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// The drawn realm maps, imported as content and served to any player who is logged in.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MapEndpointTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private sealed record Sheet(string World, string Title, int Width, int Height);

    /// <summary>
    /// Puts two sheets in the world the way anything puts one there: by importing them.
    /// </summary>
    /// <remarks>
    /// These used to be whatever the build was compiled with, which made every assertion below a
    /// statement about the Reaches - content that does not belong to this engine and is not in
    /// this repository. Imported here, the tests describe the map feature instead of describing
    /// somebody's world, and the import is itself the thing worth covering: a sheet reaches a
    /// player only if importing one serves it.
    /// </remarks>
    private static async Task ImportSheetsAsync(WebApplicationFactory<Program> app, HttpClient client)
    {
        await BuilderClient.RegisterBuilderAsync(app, client);

        var response = await client.PostAsJsonAsync(
            "/api/builder/import",
            new
            {
                // The constant, never a literal: a hardcoded version here would fail every one of
                // these the next time the format moves, and say nothing about why.
                formatVersion = BundleFormat.CurrentVersion,
                exportedAt = DateTimeOffset.UtcNow,
                scope = new { kind = "all", key = (string?)null },
                worlds = Array.Empty<object>(),
                zones = Array.Empty<object>(),
                rooms = Array.Empty<object>(),
                itemTemplates = Array.Empty<object>(),
                mobTemplates = Array.Empty<object>(),
                abilities = Array.Empty<object>(),
                spawners = Array.Empty<object>(),
                quests = Array.Empty<object>(),
                configurations = Array.Empty<object>(),
                maps = new[]
                {
                    new { worldKey = "mapfixture-one", svg = Drawing("Fixture One", 100, 400) },
                    new { worldKey = "mapfixture-two", svg = Drawing("Fixture Two", 120, 480) },
                },
            });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>The smallest thing MapSheets will accept: a root that states its size, and a title.</summary>
    private static string Drawing(string title, int width, int height) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}"><title>{title}</title></svg>""";

    [Fact]
    public async Task The_maps_are_closed_to_somebody_who_is_not_logged_in()
    {
        using var client = NewClient(postgres.App);

        var list = await client.GetAsync(new Uri("/api/maps", UriKind.Relative));
        var sheet = await client.GetAsync(new Uri("/api/maps/ossara", UriKind.Relative));

        // 401 rather than 403: there is no role to fail, only a session that is not there.
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sheet.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_player_may_read_the_maps()
    {
        // The whole point of the feature, and the one thing that separates it from everything
        // else drawn from content: no Builder role, no character id, no attunement.
        using var client = NewClient(postgres.App);
        await ImportSheetsAsync(postgres.App, client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);
        Assert.NotEmpty(sheets);
    }

    [Fact]
    public async Task Every_realm_this_build_carries_is_listed_with_a_title_and_a_size()
    {
        // Not a count of realms - that is content, and counting it here would make authoring a
        // sixth realm a failing test. What must hold is that each sheet describes itself, since
        // the client sizes its frame from these before the image arrives.
        using var client = NewClient(postgres.App);
        await ImportSheetsAsync(postgres.App, client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);

        foreach (var sheet in sheets)
        {
            Assert.False(string.IsNullOrWhiteSpace(sheet.World));
            Assert.False(string.IsNullOrWhiteSpace(sheet.Title));
            Assert.True(sheet.Width > 0, $"'{sheet.World}' reported width {sheet.Width}.");
            Assert.True(sheet.Height > 0, $"'{sheet.World}' reported height {sheet.Height}.");
        }
    }

    [Fact]
    public async Task A_sheet_comes_back_as_an_svg()
    {
        using var client = NewClient(postgres.App);
        await ImportSheetsAsync(postgres.App, client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        var world = sheets![0].World;
        var response = await client.GetAsync(new Uri($"/api/maps/{world}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sheet_the_client_already_has_is_not_sent_again()
    {
        // These are the largest things the game serves and they change only on a deploy. A client
        // that reopens the map should be answered in a few hundred bytes, which is the whole
        // reason the response carries a content-addressed ETag.
        using var client = NewClient(postgres.App);
        await ImportSheetsAsync(postgres.App, client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        var world = sheets![0].World;
        var first = await client.GetAsync(new Uri($"/api/maps/{world}", UriKind.Relative));
        var etag = first.Headers.ETag;

        Assert.NotNull(etag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/maps/{world}", UriKind.Relative));
        conditional.Headers.IfNoneMatch.Add(etag);

        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Two_realms_do_not_share_an_etag()
    {
        // The tag is a hash of the bytes. A tag derived from the build instead would be equal
        // across every sheet, and a client that had one map would be told it had all of them.
        using var client = NewClient(postgres.App);
        await ImportSheetsAsync(postgres.App, client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);
        Assert.True(sheets.Count > 1, "This assertion needs at least two realms to compare.");

        var tags = new List<EntityTagHeaderValue>();
        foreach (var sheet in sheets)
        {
            var response = await client.GetAsync(new Uri($"/api/maps/{sheet.World}", UriKind.Relative));
            tags.Add(response.Headers.ETag!);
        }

        Assert.Equal(tags.Count, tags.Select(t => t.Tag.ToString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_world_with_no_map_is_a_named_404_rather_than_an_empty_image()
    {
        // An empty 200 would render as a broken image with nothing to read, and the answer to
        // "which worlds have maps" is the list beside this route.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var response = await client.GetAsync(new Uri("/api/maps/nosuchrealm", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nosuchrealm", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheets_are_served_from_content_rather_than_read_from_disk()
    {
        // This used to assert that the drawings were compiled into the assembly, because the
        // container publishes /app/publish and there is no content/ directory beside the server
        // in the image. That constraint has not changed and neither has the answer: nothing here
        // opens a file. What changed is where the bytes come from - the database, by import, so
        // that a sheet arrives with the rooms it draws instead of with the build.
        var sheets = new MapSheets();

        Assert.Empty(sheets.All);

        sheets.Load([("testrealm", Drawing("Test Realm", 100, 400))]);

        Assert.Single(sheets.All);
        Assert.Equal("Test Realm", sheets.All[0].Title);
        Assert.Equal(400, sheets.All[0].Height);
        Assert.True(sheets.TryGet("testrealm", out var svg, out _));
        Assert.NotEmpty(svg);
    }

    [Fact]
    public void A_sheet_that_cannot_be_measured_is_skipped_rather_than_thrown()
    {
        // One unreadable drawing taken from a bundle must not stop a server from starting, which
        // is what throwing here would do now that these arrive as content. check-bundle is where
        // a malformed sheet is meant to be caught.
        var sheets = new MapSheets();

        sheets.Load(
        [
            ("broken", "<svg>no size here</svg>"),
            ("sound", Drawing("Sound", 100, 400)),
        ]);

        Assert.Single(sheets.All);
        Assert.Equal("sound", sheets.All[0].World);
    }

    [Fact]
    public void Replacing_the_sheets_drops_the_ones_that_went_away()
    {
        // An import is the only thing that reloads these, and it hands over everything the
        // database holds rather than a delta. A sheet whose world was deleted has to leave.
        var sheets = new MapSheets();

        sheets.Load([("first", Drawing("First", 100, 400)), ("second", Drawing("Second", 100, 400))]);
        sheets.Load([("second", Drawing("Second", 100, 400))]);

        Assert.Single(sheets.All);
        Assert.False(sheets.TryGet("first", out _, out _));
    }

    [Fact]
    public void A_world_key_is_matched_without_regard_to_case()
    {
        // Room keys are lowercase by construction, but this is reached from a URL somebody can
        // type, and "no map of Ossara" is a confusing thing to be told while standing in it.
        var sheets = new MapSheets();
        sheets.Load([("testrealm", Drawing("Test Realm", 100, 400))]);

        Assert.True(sheets.TryGet("TESTREALM", out _, out _));
    }
}
