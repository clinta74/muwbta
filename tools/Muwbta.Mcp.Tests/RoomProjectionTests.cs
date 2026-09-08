using System.Text;
using System.Text.Json;
using Muwbta.Mcp;

namespace Muwbta.Mcp.Tests;

/// <summary>
/// The two pieces of this server that read the builder API's JSON rather than passing it through.
/// </summary>
/// <remarks>
/// Everything else here is a proxy, which is the whole design (see the csproj): no DTOs, no
/// opinion about the shape of a room. These two are the exception, so they are the two that can be
/// wrong about it — and both of them are subtractive, which is the dangerous direction. A
/// projection that drops the wrong property and a sweep that misreads a resolved flag both return
/// something that looks like an answer.
/// </remarks>
public sealed class RoomProjectionTests
{
    private const string Rooms = """
        [
          {"key":"a.b.c","title":"A Room","flags":{"dark":true},"grid":["###"],"legend":{"#":"wall"}},
          {"key":"a.b.d","title":"Another","flags":{},"grid":["..."],"legend":{}}
        ]
        """;

    [Fact]
    public void A_projection_keeps_what_was_asked_for_and_drops_the_rest()
    {
        var projected = BuilderTools.Project(Rooms, "key,title");

        using var document = JsonDocument.Parse(projected);
        var first = document.RootElement[0];

        Assert.Equal("a.b.c", first.GetProperty("key").GetString());
        Assert.Equal("A Room", first.GetProperty("title").GetString());
        Assert.False(first.TryGetProperty("grid", out _));
        Assert.False(first.TryGetProperty("legend", out _));
        Assert.False(first.TryGetProperty("flags", out _));
    }

    [Fact]
    public void Every_row_is_still_there()
    {
        // Subtractive on properties, never on rows. A projection that also filtered would be a
        // second thing this argument does, and the caller could not tell which one lost a room.
        using var document = JsonDocument.Parse(BuilderTools.Project(Rooms, "key"));

        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public void An_unknown_property_is_ignored_rather_than_refused()
    {
        // Refusing would mean this file holding an opinion about the shape of a room, which is
        // exactly what it must not do. The caller loses a property they were never going to get.
        using var document = JsonDocument.Parse(BuilderTools.Project(Rooms, "key,soul"));
        var first = document.RootElement[0];

        Assert.Equal("a.b.c", first.GetProperty("key").GetString());
        Assert.False(first.TryGetProperty("soul", out _));
    }

    [Fact]
    public void Anything_that_is_not_a_list_comes_back_untouched()
    {
        const string single = """{"key":"a.b.c","title":"A Room"}""";

        Assert.Equal(single, BuilderTools.Project(single, "key"));
    }

    [Fact]
    public void An_empty_field_list_is_the_same_as_not_asking()
    {
        Assert.Equal(Rooms, BuilderTools.Project(Rooms, " , "));
    }

    // -----------------------------------------------------------------------
    // find_rooms
    // -----------------------------------------------------------------------

    private static string Sweep(string room, string flag, bool? wanted)
    {
        using var document = JsonDocument.Parse(room);
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            BuilderTools.WriteRoomFlag(writer, document.RootElement, flag, wanted);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private const string Inherited = """
        {
          "key":"a.b.c","title":"A Room",
          "resolved":[
            {"key":"dark","value":false,"source":"default"},
            {"key":"peaceful","value":true,"source":"zone"}
          ]
        }
        """;

    [Fact]
    public void A_sweep_reports_the_value_and_where_it_came_from()
    {
        // Where it came from is half the answer. "Peaceful because its zone says so" and "peaceful
        // because somebody flagged this room" are different facts about the same room, and only
        // one of them survives the zone being edited.
        using var document = JsonDocument.Parse(Sweep(Inherited, "peaceful", null));
        var row = document.RootElement[0];

        Assert.Equal("a.b.c", row.GetProperty("key").GetString());
        Assert.True(row.GetProperty("peaceful").GetBoolean());
        Assert.Equal("zone", row.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("peaceful", true, 1)]
    [InlineData("peaceful", false, 0)]
    [InlineData("dark", false, 1)]
    [InlineData("dark", true, 0)]
    public void The_filter_keeps_only_rooms_resolving_that_way(string flag, bool wanted, int expected)
    {
        using var document = JsonDocument.Parse(Sweep(Inherited, flag, wanted));

        Assert.Equal(expected, document.RootElement.GetArrayLength());
    }

    private const string WithClimate = """
        {
          "key":"a.b.c","title":"A Room",
          "resolved":[{"key":"climate","value":"alpine","source":"zone"}]
        }
        """;

    [Fact]
    public void A_flag_that_resolves_to_a_word_reports_the_word()
    {
        // Read as a boolean this said `false`, for every climate in every world, which looks
        // exactly like an answer. Flags stopped being all yes-or-no when `climate` was added.
        using var document = JsonDocument.Parse(Sweep(WithClimate, "climate", null));
        var row = document.RootElement[0];

        Assert.Equal("alpine", row.GetProperty("climate").GetString());
        Assert.Equal("zone", row.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_yes_or_no_filter_matches_nothing_that_resolves_to_a_word(bool wanted)
    {
        // Honest rather than helpful: "which rooms are climate-true" has no answer, so it gets no
        // rows rather than an arbitrary half of them.
        using var document = JsonDocument.Parse(Sweep(WithClimate, "climate", wanted));

        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public void A_flag_the_room_says_nothing_about_produces_no_row()
    {
        // Not a row saying false. The registry decides the default and the server puts it in
        // `resolved`; a room whose resolved list does not mention the flag at all is a room this
        // server has been given something unexpected about, and inventing an answer would hide it.
        using var document = JsonDocument.Parse(Sweep(Inherited, "indoors", null));

        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public void A_room_with_no_resolved_list_produces_no_row()
    {
        const string bare = """{"key":"a.b.c","title":"A Room"}""";

        using var document = JsonDocument.Parse(Sweep(bare, "indoors", null));

        Assert.Equal(0, document.RootElement.GetArrayLength());
    }
}
