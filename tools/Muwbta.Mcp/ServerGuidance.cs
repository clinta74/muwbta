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
        This server reads one deployment of muwbta, a text world whose content - rooms, mobs,
        items, quests, abilities - is authored through its builder API. It is read-only: every
        tool here fetches, none of them change anything.

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
        5. export_bundle to show your work: it returns the same JSON the builder's import accepts,
           so a person can diff it before anything reaches the world.

        Prose you draft is a proposal. A person reads it and decides, and this server has no way
        to apply it in any case.
        """;
}
