using System.Text.RegularExpressions;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Engine;
using Muwbta.Persistence;
using Muwbta.Server.Auth;
using Muwbta.Server.Game;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Characters;

public sealed record CreateCharacterRequest(string Name, string Path);

public sealed record CharacterResponse(
    Guid Id,
    string Name,
    string Path,
    int Level,
    long Xp,
    string RoomKey,
    DateTimeOffset? LastPlayedAt);

public static partial class CharacterEndpoints
{
    public static void MapCharacterEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/characters").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        MuwbtaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var characters = await db.Characters
            .AsNoTracking()
            .Where(c => c.AccountId == accountId && c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(characters.Select(ToResponse));
    }

    private static async Task<IResult> CreateAsync(
        CreateCharacterRequest request,
        HttpContext http,
        MuwbtaDbContext db,
        EngineOptions engineOptions,
        SessionRegistryOptions sessionOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        if (!NamePattern().IsMatch(request.Name ?? string.Empty))
        {
            return Results.BadRequest(new { error = "Name must be 3-16 letters." });
        }

        // The name every other player sees, so the one that matters most: "Admin tells you" is
        // only a forgery if nobody can be Admin (ReservedNames says why the list is what it is).
        if (ReservedNames.IsReserved(request.Name!))
        {
            return Results.BadRequest(new { error = "That name is reserved." });
        }

        // The active configuration's word list applies to names as it does to speech: a name is
        // said in every room its owner walks into.
        if (engineOptions.WordFilter.Matches(request.Name, out _))
        {
            return Results.BadRequest(new { error = "That name is not allowed here." });
        }

        // IsDefined as well as TryParse: TryParse accepts any integer string, so "42" parsed to a
        // path that is not one - a character with no abilities and a number where its path
        // should be. The role endpoint already guards the same way; this one did not.
        if (!Enum.TryParse<CharacterPath>(request.Path, ignoreCase: true, out var path)
            || !Enum.IsDefined(path))
        {
            var valid = string.Join(", ", Enum.GetNames<CharacterPath>());
            return Results.BadRequest(new { error = $"Path must be one of: {valid}." });
        }

        // Character names are globally unique, not per-account: two Kaels in one room would
        // make every targeted command ambiguous.
        if (await db.Characters.AnyAsync(c => c.Name == request.Name, cancellationToken))
        {
            return Results.Conflict(new { error = "That name is taken." });
        }

        // A roster cap, distinct from the concurrent-session cap the same options class carries.
        // Deleted characters do not count, matching the list this account can see - deleting one
        // is how a player frees a slot.
        //
        // Checked after the name and path, so somebody at the cap who also mistyped a name is told
        // about the name first. The cap is the answer they can do nothing about in this request,
        // and reporting it while a fixable problem is also present would send them away to delete
        // a character they did not need to.
        var existing = await db.Characters
            .CountAsync(c => c.AccountId == accountId && c.DeletedAt == null, cancellationToken);

        if (existing >= sessionOptions.MaxCharactersPerAccount)
        {
            return Results.Conflict(new
            {
                error = $"You already have {existing} characters, which is the limit. "
                    + "Delete one to make room.",
            });
        }

        var character = new Character
        {
            AccountId = accountId,
            Name = request.Name!,
            Path = path,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(path),
            RoomKey = engineOptions.StartingRoom,
            CreatedAt = clock.GetUtcNow(),
        };

        db.Characters.Add(character);

        // What the live configuration hands a new character (PLAN.md §4.16), saved in the same
        // transaction so a character never exists without the kit it was promised.
        var kit = await db.GameConfigurations.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.StartingKit)
            .FirstOrDefaultAsync(cancellationToken);

        if (kit is { Count: > 0 })
        {
            var keys = kit.Select(k => k.ItemKey).ToList();
            var templates = await db.ItemTemplates.AsNoTracking()
                .Where(t => keys.Contains(t.Key))
                .ToDictionaryAsync(t => t.Key, StringComparer.Ordinal, cancellationToken);

            db.ItemInstances.AddRange(StartingKitGrant.ItemsFor(kit, templates, character.Id));
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/characters/{character.Id}", ToResponse(character));
    }

    private static CharacterResponse ToResponse(Character c) =>
        new(c.Id, c.Name, c.Path.ToString(), c.Level, c.Xp, c.RoomKey.ToString(), c.LastPlayedAt);

    [GeneratedRegex("^[A-Za-z]{3,16}$")]
    private static partial Regex NamePattern();
}
