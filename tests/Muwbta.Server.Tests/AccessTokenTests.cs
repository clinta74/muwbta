using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Persistence;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// Personal access tokens (docs/PAT-AND-MCP.md, Part 1).
/// </summary>
/// <remarks>
/// The properties worth pinning are the ones a token could silently lose by being implemented as
/// "is this hash in the table": that a ban, a demotion and a password change all reach it, and
/// that it cannot leave the builder surface. Each of those is a test here rather than a comment,
/// because each is invisible when it stops working.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AccessTokenTests(PostgresFixture postgres)
{
    private HttpClient NewClient() =>
        postgres.App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>A client with no cookie, which is what a program holding a token looks like.</summary>
    private HttpClient NewBearerClient(string token)
    {
        var client = postgres.App.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<(string Secret, Guid Id)> MintAsync(
        HttpClient client,
        string scope = nameof(AccessTokenScope.BuilderWrite),
        int days = 30,
        string name = "a token")
    {
        var response = await client.PostAsJsonAsync("/api/auth/tokens", new
        {
            name,
            scope,
            expiresInDays = days,
        });

        var body = await BuilderClient.JsonAsync(response);

        return (
            body.GetProperty("secret").GetString()!,
            body.GetProperty("token").GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------------
    // Issuing
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A_player_cannot_mint_a_token()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterAsync(client);

        (await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correcthorse" }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/tokens", new
        {
            name = "nope",
            scope = "BuilderRead",
            expiresInDays = 30,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_secret_is_returned_once_and_never_stored()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var (secret, id) = await MintAsync(client);

        Assert.StartsWith("muwbta_pat_", secret, StringComparison.Ordinal);

        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
        var row = await db.AccessTokens.AsNoTracking().FirstAsync(t => t.Id == id);

        // The whole point of §3: what is stored cannot be presented.
        Assert.DoesNotContain(secret, row.SecretHash, StringComparison.Ordinal);
        Assert.NotEqual(secret, row.SecretHash);

        // And the list never carries it again.
        var list = await BuilderClient.JsonAsync(await client.GetAsync("/api/auth/tokens"));
        Assert.DoesNotContain(secret, list.ToString(), StringComparison.Ordinal);
        Assert.Equal(username, username);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3650)]
    public async Task A_token_cannot_be_given_a_life_outside_the_ceiling(int days)
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var response = await client.PostAsJsonAsync("/api/auth/tokens", new
        {
            name = "too long",
            scope = "BuilderRead",
            expiresInDays = days,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_minted_without_an_expiry()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        // No expiresInDays at all - the field is not optional, because a credential nobody has to
        // renew is one nobody revokes.
        var response = await client.PostAsJsonAsync(
            "/api/auth/tokens", new { name = "forever", scope = "BuilderRead" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // Using
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A_write_token_reads_and_writes_the_builder_api()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, _) = await MintAsync(client);

        using var bearer = NewBearerClient(secret);

        var read = await bearer.GetAsync("/api/builder/worlds");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var worldKey = BuilderClient.UniqueName("w").ToLowerInvariant();
        var write = await bearer.PostAsJsonAsync($"/api/builder/worlds/{worldKey}", new
        {
            name = "Made by a token",
            description = "Written without a cookie.",
        });

        Assert.True(write.IsSuccessStatusCode, await write.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_read_token_may_only_get()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, _) = await MintAsync(client, nameof(AccessTokenScope.BuilderRead));

        using var bearer = NewBearerClient(secret);

        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/builder/worlds")).StatusCode);

        var write = await bearer.PostAsJsonAsync(
            $"/api/builder/worlds/{BuilderClient.UniqueName("w").ToLowerInvariant()}",
            new { name = "No", description = "No." });

        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    /// <summary>
    /// The property that keeps a leaked token from becoming a permanent foothold: it cannot mint
    /// another one, cannot change the password, and cannot reach the admin surface.
    /// </summary>
    [Theory]
    [InlineData("/api/auth/tokens")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/admin/accounts")]
    [InlineData("/api/characters")]
    public async Task A_token_reaches_nothing_outside_the_builder(string path)
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        await BuilderClient.SetRoleAsync(postgres.App, username, AccountRole.Admin);

        var (secret, _) = await MintAsync(client);
        using var bearer = NewBearerClient(secret);

        // Admin, so the role is not what refuses this. The scheme is.
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_malformed_token_is_refused()
    {
        using var bearer = NewBearerClient("not-a-token");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await bearer.GetAsync("/api/builder/worlds")).StatusCode);
    }

    // ---------------------------------------------------------------------
    // The three properties a naive lookup would lose
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A_ban_stops_a_token_at_once()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, _) = await MintAsync(client);

        using var bearer = NewBearerClient(secret);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/builder/worlds")).StatusCode);

        using (var scope = postgres.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();
            var account = await db.Accounts.FirstAsync(a => a.Username == username);
            account.IsBanned = true;
            await db.SaveChangesAsync();
        }

        // No interval to wait out, unlike a cookie: the handler reads the account row every time.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await bearer.GetAsync("/api/builder/worlds")).StatusCode);
    }

    [Fact]
    public async Task A_demotion_stops_a_token_at_once()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, _) = await MintAsync(client);

        using var bearer = NewBearerClient(secret);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/builder/worlds")).StatusCode);

        await BuilderClient.SetRoleAsync(postgres.App, username, AccountRole.Player);

        // 403 rather than 401: the token authenticated fine, and the role is what refused it -
        // which is the point of reading the role off the account row instead of storing it.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bearer.GetAsync("/api/builder/worlds")).StatusCode);
    }

    [Fact]
    public async Task A_password_change_stops_every_token_the_account_holds()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var (first, _) = await MintAsync(client, name: "one");
        var (second, _) = await MintAsync(client, name: "two");

        using var one = NewBearerClient(first);
        using var two = NewBearerClient(second);

        Assert.Equal(HttpStatusCode.OK, (await one.GetAsync("/api/builder/worlds")).StatusCode);

        (await client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "correcthorse",
            newPassword = "batterystaple9",
        })).EnsureSuccessStatusCode();

        // Both, with no sweep having run: each snapshotted PasswordChangedAt at issue.
        Assert.Equal(HttpStatusCode.Unauthorized, (await one.GetAsync("/api/builder/worlds")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await two.GetAsync("/api/builder/worlds")).StatusCode);
    }

    // ---------------------------------------------------------------------
    // Revoking and listing
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Revoking_stops_a_token_and_is_idempotent()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, id) = await MintAsync(client);

        using var bearer = NewBearerClient(secret);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/builder/worlds")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/auth/tokens/{id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await bearer.GetAsync("/api/builder/worlds")).StatusCode);

        // A retry is not an error.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/auth/tokens/{id}")).StatusCode);
    }

    [Fact]
    public async Task One_builder_cannot_revoke_another_builders_token()
    {
        using var mine = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, mine);
        var (_, id) = await MintAsync(mine);

        using var theirs = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, theirs);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await theirs.DeleteAsync($"/api/auth/tokens/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_list_shows_live_tokens_and_hides_revoked_ones()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var (_, kept) = await MintAsync(client, name: "kept");
        var (_, dropped) = await MintAsync(client, name: "dropped");

        (await client.DeleteAsync($"/api/auth/tokens/{dropped}")).EnsureSuccessStatusCode();

        var list = await BuilderClient.JsonAsync(await client.GetAsync("/api/auth/tokens"));
        var ids = list.GetProperty("tokens").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid())
            .ToList();

        Assert.Contains(kept, ids);
        Assert.DoesNotContain(dropped, ids);
    }

    [Fact]
    public async Task An_account_cannot_hold_more_than_the_cap()
    {
        using var client = NewClient();
        await BuilderClient.RegisterBuilderAsync(postgres.App, client);

        var options = postgres.App.Services.GetRequiredService<Muwbta.Server.Auth.AuthOptions>();

        for (var i = 0; i < options.TokensPerAccount; i++)
        {
            await MintAsync(client, name: $"token {i}");
        }

        var overflow = await client.PostAsJsonAsync("/api/auth/tokens", new
        {
            name = "one too many",
            scope = "BuilderRead",
            expiresInDays = 30,
        });

        Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);
    }

    // ---------------------------------------------------------------------
    // The audit trail
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Issuing_and_revoking_are_audited_without_the_secret()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (secret, id) = await MintAsync(client, name: "audited");

        (await client.DeleteAsync($"/api/auth/tokens/{id}")).EnsureSuccessStatusCode();

        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Username == username);
        var rows = await db.AdminAudits.AsNoTracking()
            .Where(a => a.TargetAccountId == account.Id)
            .ToListAsync();

        Assert.Contains(rows, a => a.Action == AdminAction.TokenIssued);
        Assert.Contains(rows, a => a.Action == AdminAction.TokenRevoked);

        foreach (var row in rows)
        {
            Assert.DoesNotContain(secret, $"{row.Before} {row.After}", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_admin_can_see_and_revoke_somebody_elses_token()
    {
        using var builder = NewClient();
        var owner = await BuilderClient.RegisterBuilderAsync(postgres.App, builder);
        var (secret, id) = await MintAsync(builder, name: "theirs");

        using var admin = NewClient();
        var adminName = await BuilderClient.RegisterAsync(admin);
        await BuilderClient.SetRoleAsync(postgres.App, adminName, AccountRole.Admin);
        (await admin.PostAsJsonAsync(
            "/api/auth/login", new { username = adminName, password = "correcthorse" }))
            .EnsureSuccessStatusCode();

        var listed = await admin.GetAsync($"/api/admin/accounts/{owner}/tokens");
        var body = await BuilderClient.JsonAsync(listed);

        Assert.Contains(body.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain(secret, body.ToString(), StringComparison.Ordinal);

        (await admin.DeleteAsync($"/api/admin/tokens/{id}")).EnsureSuccessStatusCode();

        using var bearer = NewBearerClient(secret);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await bearer.GetAsync("/api/builder/worlds")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_takes_its_tokens_with_it()
    {
        using var client = NewClient();
        var username = await BuilderClient.RegisterBuilderAsync(postgres.App, client);
        var (_, id) = await MintAsync(client);

        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var account = await db.Accounts.FirstAsync(a => a.Username == username);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync();

        // Cascade, unlike admin_audit: a credential for an account that no longer exists is not a
        // record of anything, it is a key to a missing door.
        Assert.False(await db.AccessTokens.AnyAsync(t => t.Id == id));
    }
}

/// <summary>The token string's own shape, which needs no database.</summary>
public sealed class AccessTokenSecretTests
{
    [Fact]
    public void A_minted_token_carries_its_id_and_verifies_against_its_hash()
    {
        var id = Guid.CreateVersion7();
        var minted = Muwbta.Server.Auth.AccessTokenSecret.Mint(id);

        Assert.True(Muwbta.Server.Auth.AccessTokenSecret.TryParse(
            minted.Secret, out var parsed, out var secret));

        Assert.Equal(id, parsed);
        Assert.True(Muwbta.Server.Auth.AccessTokenSecret.Matches(secret, minted.SecretHash));
    }

    [Fact]
    public void Two_tokens_never_share_a_secret()
    {
        var first = Muwbta.Server.Auth.AccessTokenSecret.Mint(Guid.CreateVersion7());
        var second = Muwbta.Server.Auth.AccessTokenSecret.Mint(Guid.CreateVersion7());

        Assert.NotEqual(first.Secret, second.Secret);
        Assert.NotEqual(first.SecretHash, second.SecretHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bearer-ish")]
    [InlineData("muwbta_pat_")]
    [InlineData("muwbta_pat_nothex_secret")]
    [InlineData("muwbta_pat_0123456789abcdef0123456789abcdef")]
    [InlineData("muwbta_pat_0123456789abcdef0123456789abcdef_")]
    public void Anything_else_is_not_a_token(string? candidate) =>
        Assert.False(Muwbta.Server.Auth.AccessTokenSecret.TryParse(candidate, out _, out _));

    [Fact]
    public void A_wrong_secret_does_not_match()
    {
        var minted = Muwbta.Server.Auth.AccessTokenSecret.Mint(Guid.CreateVersion7());
        var other = Muwbta.Server.Auth.AccessTokenSecret.Mint(Guid.CreateVersion7());

        Muwbta.Server.Auth.AccessTokenSecret.TryParse(other.Secret, out _, out var otherSecret);

        Assert.False(Muwbta.Server.Auth.AccessTokenSecret.Matches(otherSecret, minted.SecretHash));
    }
}
