namespace Muwbta.Mcp;

/// <summary>
/// What the client is told about working with this server, sent once during the initialization
/// handshake.
/// </summary>
/// <remarks>
/// This is where the canon gets recommended, and it is deliberately the <em>only</em> place it is
/// pressed: nothing in this server or in the API behind it refuses a draft for sounding wrong, and
/// nothing should. A canon check in code would be a machine holding an opinion about prose, which
/// is the one judgement worth keeping with a person - and the moment it existed, "it passed" would
/// start being read as "it is good".
///
/// So the canon is offered rather than enforced: named first, explained, and put where a model
/// will see it before it starts writing. An agent that ignores it produces a zone that validates
/// and reads wrong, which is exactly the failure a human reviewer is there to catch.
///
/// Kept to workflow and to the relationships between the tools, per the protocol's own guidance -
/// the tools and resources carry their own descriptions and repeating them here would only mean
/// two places to update.
/// </remarks>
public static class ServerGuidance
{
    public const string Instructions = """
        This server authors one deployment of muwbta, a text world whose content - rooms, mobs,
        items, quests, abilities - lives behind its builder API.

        One thing it will refuse, so you are not surprised by it mid-task: a read-only token
        cannot write, whatever you call. That is the server's rule and nothing here can talk it
        round, so it is not worth retrying. Writing to the world the game is currently serving is
        allowed - a builder does that from the editor every day - unless this server was started
        with --protect-active, which says so plainly when it refuses.

        Read muwbta://canon before drafting any prose. It is the world's own account of what is
        true in it and the voice it is written in, and it is the only thing here that can tell you
        whether a piece of writing belongs. Nothing checks that for you. validate_zone is
        structural - dangling exits, missing descriptions, quest chains that cycle or cannot be
        reached - and a zone can pass every check while reading like it came from a different
        game. Use muwbta://canon/{configuration} when drafting against a world that is not the
        live one, which is the usual case.

        Suggested order of work:

        1. muwbta://canon, then list_content(kind: 'configuration') and 'world' to see what exists.
        2. list_content(kind: 'room', zone: ...) for a zone's layout - every room comes back with
           its editor coordinates and its exits, so this is the map. spawn_preview is a balance
           view, not a map.
        3. Read a few existing rooms in the zone you are extending before writing new ones. The
           zone's own prose is a better guide to its voice than any description of it, canon
           included.
        4. validate_zone and check_quest after any change, and act on what they say.
        5. Build a layout by writing each room with upsert_content, giving it editorX and editorY
           beside its neighbours, then set_exit twice for each link - once each way. Exits do not
           pair themselves, and a room with no coordinates lands on the origin along with every
           other one.
        6. export_bundle to show your work: it returns the same JSON the builder's import accepts,
           so a person can diff it before anything reaches the world.

        The world is live while you edit it. There is no draft mode and no publish step: a room
        saved is a room that exists, and a template retuned changes what is already spawned from
        it. Run where_used before changing a mob or an item that is already placed - those are
        global, so they have no world for the guard above to protect them with.

        Prose you draft is a proposal. Nothing checks whether it belongs to this world, so say
        what you wrote and let a person read it before it is treated as finished.
        """;
}
