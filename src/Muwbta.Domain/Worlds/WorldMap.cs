namespace Muwbta.Domain.Worlds;

/// <summary>
/// The rendered sheet for one world: an SVG drawn from that world's own rooms
/// (<c>tools/render-map.cs</c>), served to players who ask for the map.
/// </summary>
/// <remarks>
/// <para>
/// <b>It travels with the rooms it draws.</b> The sheets used to be compiled into the server as
/// embedded resources, on the reasoning that a build's own resources cannot go missing or be
/// edited underneath it. That is true and it guards the wrong failure: the rooms arrive by import
/// and the drawing arrived by build, so the two could disagree with nothing able to notice. Import
/// one and not the other and a player reads a map of a world that is no longer there. Carried as
/// content, both arrive in the same operation.
/// </para>
/// <para>
/// <b>A row of its own rather than a column on <see cref="World"/></b>, for two reasons that both
/// bite. <see cref="Muwbta.Engine.World.WorldState"/> holds the world entities themselves, so a
/// column here would carry a megabyte of drawing into the game loop, which never reads it. And
/// the importer builds a fresh <c>World</c> for an upsert, so a map living on that row would be
/// wiped by any unrelated edit to the world's name - silently, and only noticed by a player.
/// </para>
/// <para>
/// Title and intrinsic size are not stored. They are read back out of the document's own header
/// when it is served, so a sheet cannot claim a size it is not drawn at.
/// </para>
/// </remarks>
public sealed class WorldMap
{
    /// <summary>The world this draws, and the key it is served under. At most one sheet per world.</summary>
    public required string WorldKey { get; init; }

    /// <summary>The SVG document.</summary>
    public required string Svg { get; set; }
}
