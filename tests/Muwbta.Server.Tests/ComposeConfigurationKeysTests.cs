using System.Reflection;
using System.Text.RegularExpressions;
using Muwbta.Server.Auth;
using Muwbta.Server.Game;
using Muwbta.Server.Infrastructure;

namespace Muwbta.Server.Tests;

/// <summary>
/// Every key the example deployments set under a bound options section must name a real setting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configuration that misses its target does not fail. It does nothing.</b> Two keys sat in the
/// shipped compose files for months — <c>Sessions__MaxCharactersPerAccount</c>, which is the right
/// setting under a name missing one word, and <c>Sessions__MaxConcurrentSessions</c>, which was
/// never a setting at all. Both bound to nothing. The files read as though a five-character limit
/// and a hundred-session cap were in force; the engine default of three was, and nothing anywhere
/// said so.
/// </para>
/// <para>
/// A misspelt key is worse than a missing one, because a missing one prompts somebody to add it.
/// This is the cheapest way to make that class of mistake loud: the binder will not complain, so
/// the test does.
/// </para>
/// <para>
/// <b>It was scoped to <c>Sessions</c>, and the two keys it did not cover were both dead.</b>
/// <c>Auth__CookieName</c> and <c>Auth__SessionTimeoutMinutes</c> were set in both shipped files
/// against an <see cref="AuthOptions"/> that had neither property, while the cookie name and the
/// fortnight expiry were literals at the call site. The timeout was the more instructive of the
/// two: the compose value and the hardcoded one were the same number, so the key looked correct
/// and would have gone on looking correct until somebody changed it and nothing happened. Any
/// section bound as a whole belongs here; the list below is that set.
/// </para>
/// <para>
/// Sections deliberately absent: <c>Engine</c> is read key by key rather than bound (see
/// <see cref="EngineConfigurationBindingTests"/>), and <c>Logging</c> and <c>ConnectionStrings</c>
/// are the framework's own with shapes this has no business asserting. A test that tried to cover
/// those would either be wrong or be a second copy of the binder.
/// </para>
/// </remarks>
public sealed class ComposeConfigurationKeysTests
{
    /// <summary>The sections bound whole, and the type each binds onto.</summary>
    private static readonly (string Section, Type Options)[] BoundSections =
    [
        ("Sessions", typeof(SessionRegistryOptions)),
        ("Auth", typeof(AuthOptions)),
        ("Proxy", typeof(ProxyOptions)),
    ];

    /// <summary>The double underscore is how an environment variable spells a config section.</summary>
    private static Regex KeysUnder(string section) => new(
        @"^\s*" + Regex.Escape(section) + @"__(?<key>[A-Za-z0-9_]+)\s*:", RegexOptions.Multiline);

    public static TheoryData<string, string> DeploymentsAndSections
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var file in ExampleDeploymentFiles())
            {
                foreach (var (section, _) in BoundSections)
                {
                    data.Add(file, section);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DeploymentsAndSections))]
    public void Every_key_names_a_real_setting(string file, string section)
    {
        var options = BoundSections.Single(s => s.Section == section).Options;
        var text = File.ReadAllText(Path.Combine(RepoPath.Root(), "example", file));
        var settable = SettableNames(options).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Only the uncommented ones. A key named inside a comment is documentation - the two that
        // were removed are still described in those files, and describing them must stay legal.
        var declared = KeysUnder(section).Matches(text)
            .Where(m => !text[..m.Index].Split('\n')[^1].TrimStart().StartsWith('#'))
            .Select(m => m.Groups["key"].Value)
            .ToList();

        var unknown = declared.Where(k => !settable.Contains(k)).ToList();

        Assert.True(
            unknown.Count == 0,
            $"{file} sets {string.Join(", ", unknown)} under {section}:, which "
            + $"{options.Name} has no property for. The binder ignores keys it "
            + "does not recognise, so this would take effect nowhere and say nothing. Valid: "
            + string.Join(", ", settable.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void The_session_section_holds_exactly_the_settings_the_deployments_declare()
    {
        // Pins the shape the test above depends on. When a setting is added this fails, and
        // whoever added it decides whether the example deployments should declare it - which is
        // the conversation that never happened for the two keys that bound to nothing. It has
        // already earned its keep once: HeartbeatTimeoutSeconds arrived and this caught it.
        //
        // The two are easy to confuse and were confused: one bounds how many characters an
        // account may HAVE, the other how many may be in the world AT ONCE. The shipped compose
        // files set the first for months against a server that only implemented the second.
        Assert.Equal(
            [
                nameof(SessionRegistryOptions.HeartbeatTimeoutSeconds),
                nameof(SessionRegistryOptions.MaxCharactersPerAccount),
                nameof(SessionRegistryOptions.MaxConcurrentCharactersPerAccount),
            ],
            SettableNames(typeof(SessionRegistryOptions)));
    }

    [Fact]
    public void The_auth_section_holds_exactly_the_settings_the_deployments_declare()
    {
        // Same pin, for the section whose keys were dead. The computed properties -
        // RevalidationInterval, SessionTimeout, TokenLastUsedInterval - are absent by
        // construction: they have no setter, so the binder cannot reach them and neither can a
        // compose file.
        //
        // It earned its keep a second time when the access token settings arrived
        // (docs/PAT-AND-MCP.md): three new keys, and this is what said the example deployments
        // had not been told about them.
        Assert.Equal(
            [
                nameof(AuthOptions.CookieName),
                nameof(AuthOptions.LoginBackoffMaxSeconds),
                nameof(AuthOptions.LoginBackoffSeconds),
                nameof(AuthOptions.LoginFailuresBeforeBackoff),
                nameof(AuthOptions.RevalidationIntervalSeconds),
                nameof(AuthOptions.SessionTimeoutMinutes),
                nameof(AuthOptions.TokenLastUsedIntervalMinutes),
                nameof(AuthOptions.TokenMaxLifetimeDays),
                nameof(AuthOptions.TokensPerAccount),
            ],
            SettableNames(typeof(AuthOptions)));
    }

    [Fact]
    public void The_proxy_section_holds_exactly_the_settings_the_deployments_declare()
    {
        // The same pin, for the section that decides who may say where a request came from. A
        // setting added here without the deployments declaring it is a proxy hop nobody trusts,
        // and the symptom of that is the site-wide rate limit quietly coming back.
        Assert.Equal(
            [
                nameof(ProxyOptions.KnownNetworks),
                nameof(ProxyOptions.KnownProxies),
            ],
            SettableNames(typeof(ProxyOptions)));
    }

    [Theory]
    [MemberData(nameof(DeploymentsAndSections))]
    public void The_example_deployments_declare_every_setting(string file, string section)
    {
        // Not merely "no unknown keys": a deployment that silently dropped one of these would be
        // running an undeclared default, which is the sort of thing nobody notices until it
        // matters. Every one is named, so every one is a deliberate choice.
        var text = File.ReadAllText(Path.Combine(RepoPath.Root(), "example", file));

        // The GPU variant layers onto the base file rather than repeating its environment, so it
        // legitimately declares nothing here.
        if (!text.Contains(section + "__", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var name in SettableNames(BoundSections.Single(s => s.Section == section).Options))
        {
            Assert.Contains($"{section}__{name}", text, StringComparison.Ordinal);
        }
    }

    private static List<string> SettableNames(Type options) =>
        [.. options
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> ExampleDeploymentFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(RepoPath.Root(), "example"), "docker-compose*.yml")
            .Select(Path.GetFileName)
            .OfType<string>();
}
