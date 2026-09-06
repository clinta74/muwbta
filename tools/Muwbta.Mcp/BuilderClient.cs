using System.Net;
using System.Net.Http.Headers;
using ModelContextProtocol;

namespace Muwbta.Mcp;

/// <summary>
/// Reads the builder API. GET only, by construction.
/// </summary>
/// <remarks>
/// There is no method that sends anything but a GET, and that is Phase A's safety model in its
/// entirety (docs/PAT-AND-MCP.md): the agent is pointed at a live world holding a real builder's
/// session cookie, so "it cannot write" needs to be a property of this file rather than a promise
/// about which tools were registered.
///
/// Responses are passed through as text, unparsed. The API already answers in the shape its own
/// client understands, and re-modelling it here would be a second opinion about what a room is -
/// the drift tools/Muwbta.Balance's csproj comment describes, in a project that has even less
/// excuse for it.
///
/// Failures throw <see cref="McpException"/> rather than anything of this project's own. That is
/// the SDK's one channel for a message the model is allowed to read: every other exception type is
/// replaced with "An error occurred invoking 'x'" before it leaves the process, which sends the
/// agent round the same loop with the same broken cookie. The messages below are written to be
/// read by whatever is holding the other end.
/// </remarks>
public sealed class BuilderClient
{
    private readonly HttpClient http;
    private readonly BuilderOptions options;

    public BuilderClient(HttpClient http, BuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        this.http = http;
        this.options = options;
        http.BaseAddress = options.BaseAddress;
        http.Timeout = options.Timeout;
        http.DefaultRequestHeaders.Add("Cookie", $"{options.CookieName}={options.CookieValue}");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// GETs a path under <c>/api/builder</c> and returns the body, or throws with something the
    /// agent could act on.
    /// </summary>
    public async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Overwhelmingly "the server is not running", which is worth saying plainly - an agent
            // told only that something failed will retry, and retrying a refused connection is the
            // most expensive way to learn nothing.
            throw new McpException(
                $"Could not reach the muwbta server at {options.BaseAddress}. Is it running, and is "
                + $"MUWBTA_URL right? ({ex.Message})", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new McpException(
                $"The server at {options.BaseAddress} did not answer within "
                + $"{options.Timeout.TotalSeconds:0}s. Raise MUWBTA_TIMEOUT if this is a large export.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? body
                : throw new McpException(Explain(response, path, body));
        }
    }

    /// <summary>
    /// The failure messages an agent will actually read. A bare "401" sends it round the loop
    /// again with the same cookie; naming the fix ends the attempt instead.
    /// </summary>
    private static string Explain(HttpResponseMessage response, string path, string body) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Not signed in. The session cookie in MUWBTA_COOKIE has expired or is wrong - "
                + "sign in to the builder in a browser and copy the new value. Nothing this server "
                + "does can renew it, so retrying will not help.",

            HttpStatusCode.Forbidden =>
                "Signed in, but that account does not hold the Builder role. An administrator "
                + "grants it from the Accounts tab. Retrying will not help.",

            HttpStatusCode.NotFound =>
                $"No such thing at '{path}'."
                + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" The server said: {Trim(body)}"),

            HttpStatusCode.TooManyRequests =>
                "Rate limited by the builder policy"
                + (response.Headers.RetryAfter?.Delta is { } wait
                    ? $"; it will let the next call through in about {(int)wait.TotalSeconds}s."
                    : ". Slow down and try again."),

            _ => $"The server answered {(int)response.StatusCode} {response.StatusCode} for '{path}'."
                + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {Trim(body)}"),
        };

    private static string Trim(string body) =>
        body.Length <= 400 ? body.Trim() : body[..400].Trim() + "...";

    /// <summary>Builds a query string from the parameters that were actually given.</summary>
    public static string Query(params (string Name, string? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var given = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();

        return given.Length == 0 ? string.Empty : "?" + string.Join("&", given);
    }
}
