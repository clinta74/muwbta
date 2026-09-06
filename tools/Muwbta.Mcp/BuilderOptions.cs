using System.Globalization;

namespace Muwbta.Mcp;

/// <summary>
/// Where the builder API is and what to authenticate with, read from the environment.
/// </summary>
/// <remarks>
/// The environment rather than a file or a command line, because an MCP server is launched by the
/// agent's own configuration and that is the one channel every client agrees on. It also keeps the
/// cookie out of shell history and out of the repository, which is the whole reason it is not a
/// flag.
/// </remarks>
public sealed record BuilderOptions(
    Uri BaseAddress,
    string CookieName,
    string? CookieValue,
    string? Token,
    TimeSpan Timeout,
    bool ProtectActive)
{
    /// <summary>Matches <c>AuthOptions.CookieName</c>, whose default is the same string.</summary>
    public const string DefaultCookieName = "muwbta.session";

    public const string Usage = """
        Environment:
          MUWBTA_URL          Base address of the server. Default http://localhost:5050
          MUWBTA_TOKEN        A personal access token (muwbta_pat_...). The one to use.
                              Builder > Setup > Access tokens mints one.
          MUWBTA_COOKIE       A session cookie value, if there is no token yet. Expires.
          MUWBTA_COOKIE_NAME  Cookie name, if the deployment renamed it. Default muwbta.session.
          MUWBTA_TIMEOUT      Request timeout in seconds. Default 30.

        Flags:
          --protect-active    Refuse writes to the worlds the active configuration serves. Off by
                              default, because a builder editing the live world is what the builder
                              already does - this is for pointing an agent at a server people are
                              playing on.

        One of MUWBTA_TOKEN or MUWBTA_COOKIE is required, and a token is better: it is meant to be
        held by a program, it reaches the builder API and nothing else, and it does not expire
        the moment somebody signs out of a browser.
        """;

    public static BuilderOptions FromEnvironment(string[] args) =>
        From(Environment.GetEnvironmentVariable, args);

    /// <summary>
    /// The parsing, given a way to read a variable.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="FromEnvironment"/> so the rules can be tested without a test that
    /// mutates the process environment - which passes alone and fails beside its neighbours, and
    /// takes an afternoon to recognise as the reason.
    /// </remarks>
    public static BuilderOptions From(Func<string, string?> read, params string[] args)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(args);

        var url = read("MUWBTA_URL") ?? "http://localhost:5050";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException($"MUWBTA_URL is not a valid absolute URL: '{url}'.");
        }

        var token = read("MUWBTA_TOKEN")?.Trim();
        var cookie = read("MUWBTA_COOKIE");

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException("MUWBTA_TOKEN or MUWBTA_COOKIE is required.");
        }

        // A pasted cookie often arrives as the whole header - "muwbta.session=abc" - because that
        // is what the dev tools copy button gives. Taking the value off it is one line here and a
        // baffling 401 otherwise.
        var name = read("MUWBTA_COOKIE_NAME") ?? DefaultCookieName;
        var prefix = name + "=";

        if (cookie is not null && cookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            cookie = cookie[prefix.Length..];
        }

        var timeout = TimeSpan.FromSeconds(30);

        if (read("MUWBTA_TIMEOUT") is { Length: > 0 } raw)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                || seconds <= 0)
            {
                throw new InvalidOperationException($"MUWBTA_TIMEOUT must be a positive number of seconds: '{raw}'.");
            }

            timeout = TimeSpan.FromSeconds(seconds);
        }

        // Off by default, which is the same answer the builder UI gives: an account with the
        // Builder role edits the live world, and this holds a token for exactly such an account.
        // Opt in when an agent is pointed at a server people are actually playing on.
        var protectActive = args.Contains("--protect-active", StringComparer.Ordinal);

        return new BuilderOptions(
            baseAddress,
            name,
            string.IsNullOrWhiteSpace(cookie) ? null : cookie.Trim(),
            string.IsNullOrWhiteSpace(token) ? null : token,
            timeout,
            protectActive);
    }
}
