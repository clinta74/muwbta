using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Engine;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// The word list belongs to the server: written from the builder, read back, live at once, and
/// unmoved by activating a different world.
/// </summary>
/// <remarks>
/// Its own host, because activating a configuration changes what the running loop obeys and the
/// shared host's other tests assume the starter one. The matching itself is tested on
/// <c>WordFilter</c> and the five speech doors in the Engine; this is the plumbing from the panel
/// to the options, and the one door the Engine cannot see, which is a new character's name.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class BlockedWordsTests(PostgresFixture postgres) : IDisposable
{
    private readonly MuwbtaAppFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_list_is_written_read_back_and_live_at_once()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(_factory, client);

        var saved = await client.PutAsJsonAsync(
            new Uri("/api/builder/moderation", UriKind.Relative),
            new { blockedWords = "blort\nzarg" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var read = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/builder/moderation", UriKind.Relative));

        Assert.Equal("blort\nzarg", read.GetProperty("blockedWords").GetString());

        // Live with no activation and no restart. There is nothing to activate: one list, one
        // server, and the edit reaches the loop through the same path every content change does.
        var options = _factory.Services.GetRequiredService<EngineOptions>();

        Assert.Equal("blort\nzarg", options.BlockedWords);
        Assert.True(options.WordFilter.Matches("what a zarg", out _));
    }

    [Fact]
    public async Task Activating_a_different_world_leaves_the_list_alone()
    {
        // The reason this moved off GameConfiguration. It used to be a column there, so a server
        // with two configurations had two lists and switching realms switched the moderation
        // policy - which nobody chose, and which would show up as a filter that stopped working
        // for reasons no one could connect to the world they had just loaded.
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(_factory, client);

        await client.PutAsJsonAsync(
            new Uri("/api/builder/moderation", UriKind.Relative),
            new { blockedWords = "blort" });

        var key = $"words-{Guid.NewGuid():N}"[..24];

        var created = await client.PostAsJsonAsync($"/api/builder/configurations/{key}", new
        {
            name = "Somewhere else",
            description = "Authored by a test.",
            startingRoomKey = "aldenmoor.millbrook.north-gate",
            welcomeMessage = "Welcome, {name}.",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var activated = await client.PostAsync(
            new Uri($"/api/builder/configurations/{key}/activate", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        var options = _factory.Services.GetRequiredService<EngineOptions>();

        Assert.Equal("blort", options.BlockedWords);
        Assert.True(options.WordFilter.Matches("blort", out _));
    }

    [Fact]
    public async Task A_name_on_the_active_list_cannot_be_a_character()
    {
        // The one door the Engine cannot see. Set directly rather than through activation, so
        // this test says nothing about the plumbing the one above already covers.
        _factory.Services.GetRequiredService<EngineOptions>().BlockedWords = "blort";

        using var client = NewClient();
        await BuilderClient.RegisterAsync(client);

        var refused = await client.PostAsJsonAsync("/api/characters", new { name = "Blort", path = "Warden" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Whole words: a name that merely contains one is somebody's name.
        var allowed = await client.PostAsJsonAsync("/api/characters", new { name = "Blortimer", path = "Warden" });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}
