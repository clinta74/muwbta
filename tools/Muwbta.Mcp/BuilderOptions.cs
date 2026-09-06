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
public sealed record BuilderOptions(Uri BaseAddress, string CookieName, string CookieValue, TimeSpan Timeout)
{
    /// <summary>Matches <c>AuthOptions.CookieName</c>, whose default is the same string.</summary>
    public const string DefaultCookieName = "muwbta.session";

    public const string Usage = """
        Environment:
          MUWBTA_URL          Base address of the server. Default http://localhost:5000
          MUWBTA_COOKIE       The value of the session cookie for a Builder account. Required.
                              In the browser's dev tools, Application > Cookies > muwbta.session.
          MUWBTA_COOKIE_NAME  Cookie name, if the deployment renamed it. Default muwbta.session.
          MUWBTA_TIMEOUT      Request timeout in seconds. Default 30.

        The cookie expires the way any session does. When every call starts answering
        "not signed in", sign in again in the browser and copy the new value.
        """;

    public static BuilderOptions FromEnvironment() =>
        From(Environment.GetEnvironmentVariable);

    /// <summary>
    /// The parsing, given a way to read a variable.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="FromEnvironment"/> so the rules can be tested without a test that
    /// mutates the process environment - which passes alone and fails beside its neighbours, and
    /// takes an afternoon to recognise as the reason.
    /// </remarks>
    public static BuilderOptions From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var url = read("MUWBTA_URL") ?? "http://localhost:5000";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException($"MUWBTA_URL is not a valid absolute URL: '{url}'.");
        }

        var cookie = read("MUWBTA_COOKIE");

        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException("MUWBTA_COOKIE is required.");
        }

        // A pasted cookie often arrives as the whole header - "muwbta.session=abc" - because that
        // is what the dev tools copy button gives. Taking the value off it is one line here and a
        // baffling 401 otherwise.
        var name = read("MUWBTA_COOKIE_NAME") ?? DefaultCookieName;
        var prefix = name + "=";

        if (cookie.StartsWith(prefix, StringComparison.Ordinal))
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

        return new BuilderOptions(baseAddress, name, cookie.Trim(), timeout);
    }
}
