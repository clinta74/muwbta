using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The world's canon, served as a resource rather than pushed into every request
/// (docs/PAT-AND-MCP.md §12).
/// </summary>
/// <remarks>
/// A resource because the agent should read it when it needs it. The text is the active
/// configuration's canon - the same string <c>Canon.Resolve</c> hands the local assist - so an
/// agent authoring through MCP and a builder using the draft button in the web editor are working
/// from one source. Two copies of the canon is two worlds, eventually.
/// </remarks>
[McpServerResourceType]
public static class CanonResources
{
    [McpServerResource(UriTemplate = "muwbta://canon", Name = "canon", MimeType = "text/markdown")]
    [Description("""
        The active configuration's canon: what is true in this world, in the voice it is written
        in. Read it before drafting anything - it is the only thing here that says whether a piece
        of prose belongs, because validate_zone checks structure and nothing else.
        """)]
    public static async Task<string> ActiveCanonAsync(
        BuilderClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var list = await client
            .GetAsync("/api/builder/configurations", cancellationToken)
            .ConfigureAwait(false);

        var key = ActiveKey(list)
            ?? throw new McpException(
                "No configuration is active, so there is no canon to read. The server is running on "
                + "its configured fallback; the Setup tab is where a configuration is activated. "
                + "muwbta://canon/{configuration} reads a named one.");

        return await client
            .GetAsync($"/api/builder/configurations/{Uri.EscapeDataString(key)}/canon", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Any configuration's canon by key, for authoring against something other than what is live.
    /// </summary>
    /// <remarks>
    /// A template beside the direct resource rather than instead of it. Drafting happens in a world
    /// that is not the active one - that is the whole shape of the workflow - and an agent that
    /// could only read the live canon would be writing a draft world in the voice of the running
    /// one. <c>list_content(kind: 'configuration')</c> is where the keys come from.
    /// </remarks>
    [McpServerResource(
        UriTemplate = "muwbta://canon/{configuration}",
        Name = "canon-for-configuration",
        MimeType = "text/markdown")]
    [Description("""
        One named configuration's canon, for drafting against a world that is not the live one.
        The keys come from list_content(kind: 'configuration').
        """)]
    public static async Task<string> CanonForAsync(
        BuilderClient client,
        string configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new McpException("A configuration key is required. muwbta://canon reads the active one.");
        }

        return await client
            .GetAsync(
                $"/api/builder/configurations/{Uri.EscapeDataString(configuration)}/canon",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? ActiveKey(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("configurations", out var rows))
        {
            return null;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("isActive", out var active)
                && active.ValueKind == JsonValueKind.True
                && row.TryGetProperty("key", out var key))
            {
                return key.GetString();
            }
        }

        return null;
    }
}
