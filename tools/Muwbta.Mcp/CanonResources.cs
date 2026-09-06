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
                + "its configured fallback; the Setup tab is where a configuration is activated.");

        return await client
            .GetAsync($"/api/builder/configurations/{Uri.EscapeDataString(key)}/canon", cancellationToken)
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
