using System.Globalization;
using System.Security.Cryptography;
using Muwbta.Domain.Accounts;

namespace Muwbta.Server.Auth;

/// <summary>
/// The shape of a personal access token, and the only place it is made or read
/// (docs/PAT-AND-MCP.md §2, §3).
/// </summary>
/// <remarks>
/// <c>muwbta_pat_&lt;32 hex&gt;_&lt;43 char base64url&gt;</c>. Three parts, each earning its place:
///
/// <list type="bullet">
/// <item><description>The prefix makes a leaked token greppable in a log and recognisable to a
/// secret scanner. Eleven characters, and the cheapest thing here.</description></item>
/// <item><description>The id half makes verification an indexed primary-key lookup instead of a
/// scan that hashes every row.</description></item>
/// <item><description>The secret half is 32 bytes from the CSPRNG.</description></item>
/// </list>
/// </remarks>
public static class AccessTokenSecret
{
    public const string Prefix = "muwbta_pat_";

    /// <summary>32 bytes, which is 43 base64url characters unpadded.</summary>
    private const int SecretBytes = 32;

    /// <summary>A token and its parts, handed back once at creation.</summary>
    /// <param name="Id">The public half, which is also the row's primary key.</param>
    /// <param name="Secret">The full token string. Shown once and never stored.</param>
    /// <param name="SecretHash">What goes in the row.</param>
    public sealed record Minted(Guid Id, string Secret, string SecretHash);

    public static Minted Mint(Guid id)
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        var secret = Base64Url(bytes);

        return new Minted(id, $"{Prefix}{id:N}_{secret}", Hash(secret));
    }

    /// <summary>
    /// Splits a presented token, or returns false. Does no I/O and touches no database - a
    /// malformed token is refused before anything is looked up.
    /// </summary>
    public static bool TryParse(string? token, out Guid id, out string secret)
    {
        id = default;
        secret = string.Empty;

        if (token is null || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = token[Prefix.Length..];
        var split = rest.IndexOf('_', StringComparison.Ordinal);

        if (split <= 0 || split == rest.Length - 1)
        {
            return false;
        }

        if (!Guid.TryParseExact(rest[..split], "N", out id))
        {
            return false;
        }

        secret = rest[(split + 1)..];
        return secret.Length > 0;
    }

    public static string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>Constant-time, because the comparison is against a stored credential.</summary>
    public static bool Matches(string secret, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(storedHash);

        var presented = System.Text.Encoding.UTF8.GetBytes(Hash(secret));
        var stored = System.Text.Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(presented, stored);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Formats an expiry for an audit row, which stores plain text.</summary>
    public static string Describe(string name, AccessTokenScope scope, DateTimeOffset expires) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} ({scope}, expires {expires:yyyy-MM-dd})");
}
