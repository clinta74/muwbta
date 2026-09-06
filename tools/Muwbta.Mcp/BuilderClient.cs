using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol;

namespace Muwbta.Mcp;

/// <summary>
/// Talks to the builder API.
/// </summary>
/// <remarks>
/// Phase A made "cannot write" a property of this file - there was no method that sent anything
/// but a GET. Phase C removes that, so the guarantee moves to where it can still be structural:
/// the <b>token's scope</b>, checked by the server on every request, and <see cref="WorldGuard"/>,
/// which refuses to touch a world the running game is serving. A BuilderRead token still cannot
/// write however this client is called, because the refusal is not this client's to make.
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
        // A token when there is one. It is the credential this was built for: a cookie is a
        // browser's, it expires when its session does, and it carries the whole account rather
        // than the builder surface alone.
        if (options.Token is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            http.DefaultRequestHeaders.Add("Cookie", $"{options.CookieName}={options.CookieValue}");
        }
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// GETs a path under <c>/api/builder</c> and returns the body, or throws with something the
    /// agent could act on.
    /// </summary>
    public Task<string> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, cancellationToken);

    /// <summary>Whether a path exists, so an upsert can tell a create from an update.</summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await GetAsync(path, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (McpException) when (LastStatus == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>The status of the most recent response, for <see cref="ExistsAsync"/> alone.</summary>
    private HttpStatusCode? LastStatus { get; set; }

    /// <summary>
    /// Sends a request and returns the body, or throws with something the agent could act on.
    /// </summary>
    public async Task<string> SendAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);

        // A 429 here is pacing, not refusal, and the two limits an author meets are both tuned for
        // a person: the builder bucket for someone editing quickly, and DigThrottle for a held-down
        // movement key carving forty rooms. An agent laying out a zone digs six times in half a
        // second and is entirely legitimate, so waiting is the correct response and giving up is
        // not - a dig refused leaves the room unlinked and the zone quietly broken, which is what
        // this cost the first time it happened.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(method, path, json, cancellationToken).ConfigureAwait(false);
            }
            catch (McpException) when (LastStatus == HttpStatusCode.TooManyRequests && attempt < RetryLimit)
            {
                await Task.Delay(RetryWait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Twice is enough for both limits; more would be waiting out a real problem.</summary>
    private const int RetryLimit = 2;

    /// <summary>Longer than DigThrottle's two seconds, and long enough to refill the bucket.</summary>
    private static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(2500);

    private async Task<string> SendOnceAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {

        using var request = new HttpRequestMessage(method, path);

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
            LastStatus = response.StatusCode;
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
    private string Explain(HttpResponseMessage response, string path, string body) =>
        response.StatusCode switch
        {
            // The server's own words first, always. It distinguishes an expired token from a
            // revoked one from a read-only token pointed at a write, and those want different
            // things done about them - the generic list below sends an agent to mint a
            // replacement when what it actually needed was a wider scope.
            HttpStatusCode.Unauthorized when Reason(body) is { Length: > 0 } reason => reason,

            HttpStatusCode.Unauthorized => options.Token is { Length: > 0 }
                ? "The access token in MUWBTA_TOKEN was refused, and the server gave no reason. It "
                    + "has most likely expired or been revoked; a new one is minted at "
                    + "Builder > Setup > Access tokens."
                : "Not signed in. The session cookie in MUWBTA_COOKIE has expired or is wrong - "
                    + "sign in to the builder in a browser and copy the new value. A personal access "
                    + "token in MUWBTA_TOKEN does not have this problem. Retrying will not help.",

            HttpStatusCode.Forbidden =>
                "Signed in, but that account does not hold the Builder role. An administrator "
                + "grants it from the Accounts tab. Retrying will not help.",

            HttpStatusCode.NotFound =>
                $"No such thing at '{path}'."
                + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" The server said: {Trim(body)}"),

            HttpStatusCode.Conflict =>
                $"Something already exists at '{path}'. The server said: {Trim(body)}",

            HttpStatusCode.TooManyRequests =>
                (Reason(body) ?? "Rate limited by the builder policy.")
                + (response.Headers.RetryAfter?.Delta is { } wait
                    ? $" It will let the next call through in about {(int)wait.TotalSeconds}s."
                    : " Waiting and retrying is the right response; this client already did, twice."),

            _ => $"The server answered {(int)response.StatusCode} {response.StatusCode} for '{path}'."
                + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {Trim(body)}"),
        };

    /// <summary>
    /// The <c>error</c> an endpoint reported, or nothing when the body is not shaped that way.
    /// </summary>
    /// <remarks>
    /// Every refusal in this API answers <c>{ "error": "..." }</c>, and those messages are written
    /// to be read - AccessTokenHandler in particular says exactly which of six things went wrong.
    /// Replacing that with a guess would throw away the useful half of the response.
    /// </remarks>
    private static string? Reason(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
