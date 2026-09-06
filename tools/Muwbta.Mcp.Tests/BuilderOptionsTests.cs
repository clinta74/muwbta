using Muwbta.Mcp;

namespace Muwbta.Mcp.Tests;

public class BuilderOptionsTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] set)
    {
        var map = set.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void Defaults_to_a_local_server_and_the_shipped_cookie_name()
    {
        var options = BuilderOptions.From(Env(("MUWBTA_COOKIE", "abc")));

        Assert.Equal(new Uri("http://localhost:5050"), options.BaseAddress);
        Assert.Equal(BuilderOptions.DefaultCookieName, options.CookieName);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }

    [Fact]
    public void Refuses_to_start_without_a_credential() =>
        Assert.Contains(
            "MUWBTA_TOKEN",
            Assert.Throws<InvalidOperationException>(() => BuilderOptions.From(Env())).Message,
            StringComparison.Ordinal);

    [Fact]
    public void A_token_alone_is_enough()
    {
        var options = BuilderOptions.From(Env(("MUWBTA_TOKEN", "muwbta_pat_abc_def")));

        Assert.Equal("muwbta_pat_abc_def", options.Token);
        Assert.Null(options.CookieValue);
    }

    /// <summary>
    /// Both set is a machine mid-migration from the cookie it started with. The token wins,
    /// because it is the credential meant for a program - and silently preferring the worse one
    /// would be a puzzle to debug.
    /// </summary>
    [Fact]
    public void A_token_wins_over_a_cookie()
    {
        var options = BuilderOptions.From(Env(
            ("MUWBTA_TOKEN", "muwbta_pat_abc_def"),
            ("MUWBTA_COOKIE", "stale")));

        Assert.Equal("muwbta_pat_abc_def", options.Token);
    }

    /// <summary>
    /// What the dev tools copy button actually puts on the clipboard. Pasting it whole used to be
    /// a 401 with nothing to explain it.
    /// </summary>
    [Fact]
    public void Takes_the_value_off_a_pasted_name_equals_value()
    {
        var options = BuilderOptions.From(Env(("MUWBTA_COOKIE", "muwbta.session=abc123")));

        Assert.Equal("abc123", options.CookieValue);
    }

    [Fact]
    public void Strips_only_the_configured_name()
    {
        var options = BuilderOptions.From(Env(
            ("MUWBTA_COOKIE", "other.session=abc123"),
            ("MUWBTA_COOKIE_NAME", "muwbta.session")));

        Assert.Equal("other.session=abc123", options.CookieValue);
    }

    [Fact]
    public void Honours_a_renamed_cookie()
    {
        var options = BuilderOptions.From(Env(
            ("MUWBTA_COOKIE", "beta.session=xyz"),
            ("MUWBTA_COOKIE_NAME", "beta.session")));

        Assert.Equal("beta.session", options.CookieName);
        Assert.Equal("xyz", options.CookieValue);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/api/builder")]
    public void Refuses_a_url_that_is_not_absolute(string url) =>
        Assert.Contains(
            "MUWBTA_URL",
            Assert.Throws<InvalidOperationException>(
                () => BuilderOptions.From(Env(("MUWBTA_COOKIE", "abc"), ("MUWBTA_URL", url)))).Message,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("thirty")]
    public void Refuses_a_timeout_that_is_not_a_positive_number(string timeout) =>
        Assert.Contains(
            "MUWBTA_TIMEOUT",
            Assert.Throws<InvalidOperationException>(
                () => BuilderOptions.From(Env(("MUWBTA_COOKIE", "abc"), ("MUWBTA_TIMEOUT", timeout)))).Message,
            StringComparison.Ordinal);

    [Fact]
    public void Accepts_a_longer_timeout_for_large_exports() =>
        Assert.Equal(
            TimeSpan.FromSeconds(120),
            BuilderOptions.From(Env(("MUWBTA_COOKIE", "abc"), ("MUWBTA_TIMEOUT", "120"))).Timeout);
}

public class QueryTests
{
    [Fact]
    public void Is_empty_when_nothing_was_given() =>
        Assert.Equal(string.Empty, BuilderClient.Query(("world", null), ("zone", "  ")));

    [Fact]
    public void Joins_only_the_parameters_that_were_given() =>
        Assert.Equal(
            "?world=aldenmoor&zone=ald-tavern",
            BuilderClient.Query(("world", "aldenmoor"), ("zone", "ald-tavern")));

    [Fact]
    public void Escapes_values() =>
        Assert.Equal("?zone=a%20b%26c", BuilderClient.Query(("zone", "a b&c")));
}
