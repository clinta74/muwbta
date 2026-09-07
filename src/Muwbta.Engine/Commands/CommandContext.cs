using Muwbta.Domain.Worlds;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Quests;
using Muwbta.Engine.Time;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Commands;

/// <summary>
/// Everything a command handler is allowed to touch. Note the absence of
/// RoomLayoutService: handlers reach presentation only through <see cref="View"/>, so no
/// rule can ever branch on where something is drawn (PLAN.md §4.2).
/// </summary>
public sealed class CommandContext
{
    public required PlayerActor Actor { get; init; }

    public required WorldState World { get; init; }

    public required PlayerView View { get; init; }

    /// <summary>
    /// The world-editing path for builder commands (PLAN.md §7.6). Null in contexts that do
    /// not permit editing at all, which is why <see cref="Edit"/> refuses rather than throws.
    /// </summary>
    public LoopWorldEditor? Editor { get; init; }

    /// <summary>
    /// Where account administration is handed off (PLAN.md §7.7). Null in contexts that do not
    /// permit it, which is why the admin commands check rather than assume.
    /// </summary>
    public IAccountAdminQueue? AdminQueue { get; init; }

    /// <summary>Where items are handed off to be persisted. Null if item saving is not available.</summary>
    public IItemSaveQueue? ItemSaveQueue { get; init; }

    /// <summary>
    /// Item templates, for handlers that need an item's declared slot or description - an
    /// <see cref="Domain.Items.ItemInstance"/> caches only its key. Null if unavailable.
    /// </summary>
    public ItemTemplateCache? ItemTemplates { get; init; }

    /// <summary>
    /// Mob templates, for handlers that need a mob's behavior - a <see cref="Domain.Inhabitants.Mob"/>
    /// caches only its key. Null if unavailable, which is why callers treat a missing template as
    /// "no special behavior" rather than refusing.
    /// </summary>
    public MobTemplateCache? MobTemplates { get; init; }

    /// <summary>Engine tuning the handlers read, such as the shop sellback rate.</summary>
    public EngineOptions? Options { get; init; }

    /// <summary>
    /// The sky and the calendar, for <c>sky</c>. Null where the engine runs without it, which is
    /// why the handler checks: a missing optional dependency must cost a verb, not a pulse.
    /// </summary>
    public Systems.WeatherSystem? Weather { get; init; }

    /// <summary>
    /// The pending-shutdown countdown, for the admin verb that sets it. Null where the world
    /// cannot be closed from inside itself, which is why the handler checks rather than assumes.
    /// </summary>
    public Systems.ShutdownSchedule? Shutdown { get; init; }

    /// <summary>
    /// The game clock, for handlers that have to ask whether a timed effect is still running.
    /// </summary>
    public IGameClock? Clock { get; init; }

    /// <summary>Quest definitions the command layer reads. Null if quests are unavailable.</summary>
    public QuestCache? Quests { get; init; }

    /// <summary>
    /// The ability table, for the handler that has to name what a level-up granted. Null in a
    /// host that never loaded abilities, which is why the announcement degrades to passives
    /// rather than refusing.
    /// </summary>
    public Abilities.AbilityCache? Abilities { get; init; }

    /// <summary>Where quest progress is handed off to be persisted.</summary>
    public ICharacterQuestSaveQueue? QuestSaveQueue { get; init; }

    /// <summary>Applies a content edit and queues it for persistence.</summary>
    public MutationResult Edit(WorldChange change) =>
        Editor is null
            ? MutationResult.Fail(MutationError.Invalid, "World editing is not available here.")
            : Editor.Apply(change, Actor.Character.AccountId);

    /// <summary>The verb as the player typed it, used in error messages.</summary>
    public required string Verb { get; init; }

    /// <summary>Everything after the verb, trimmed. Empty string when there were no arguments.</summary>
    public required string Argument { get; init; }

    /// <summary>Set by a handler to have the loop remove this player after the command.</summary>
    public LeaveReason? LeaveRequested { get; set; }

    /// <summary>
    /// Other characters the loop should remove after this command, and why (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="LeaveRequested"/>, which only ever means "the person who typed
    /// this". Removing someone else has to go back through the loop rather than happening in the
    /// handler, because leaving the world is more than dropping them from
    /// <see cref="WorldState"/> — there is a save, a channel to close, and a room to redraw, and a
    /// second copy of that list would drift from the first.
    /// </remarks>
    private readonly List<(Guid CharacterId, LeaveReason Reason)> _removals = [];

    /// <inheritdoc cref="_removals"/>
    public IReadOnlyList<(Guid CharacterId, LeaveReason Reason)> RemovalsRequested => _removals;

    /// <summary>Asks the loop to take another character out of the world after this command.</summary>
    public void RequestRemoval(Guid characterId, LeaveReason reason) =>
        _removals.Add((characterId, reason));

    /// <summary>Rooms marked for refresh after command completes.</summary>
    internal HashSet<RoomKey> RoomsToRefresh { get; } = [];

    public bool HasArgument => !string.IsNullOrWhiteSpace(Argument);

    /// <summary>Mark a room to be refreshed for all occupants after this command completes.</summary>
    public void MarkRoomForRefresh(RoomKey roomKey) => RoomsToRefresh.Add(roomKey);

    /// <summary>
    /// True when this player is silenced, having told them so (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// Asked by every verb that carries words to another player — <c>say</c>, <c>emote</c>,
    /// <c>tell</c>, <c>reply</c>, <c>chat</c>, <c>gtell</c> — because a mute that only covered the
    /// global channel would be a mute in name only.
    ///
    /// It reports rather than swallowing. Silently dropping the message is worse than refusing it:
    /// the player carries on talking to a room that cannot hear them, which is a crueller
    /// punishment than the one that was chosen and looks like a bug besides.
    /// </remarks>
    public bool RefusedForMute()
    {
        var now = Clock?.UtcNow ?? DateTimeOffset.UtcNow;

        if (!Actor.IsMuted(now))
        {
            return false;
        }

        Reply($"You have been muted until {Actor.MutedUntil:u}. Nothing you say leaves your lips.", "bad");
        return true;
    }

    /// <summary>
    /// Whether <paramref name="text"/> contains a word the active configuration refuses, replying
    /// so if it does. The same shape as <see cref="RefusedForMute"/>, and called at the same five
    /// doors, because a filter with a way around it is a filter in name only.
    /// </summary>
    /// <remarks>
    /// Refused rather than masked. Replacing the word with asterisks would send the sentence on
    /// with a hole in it that everybody can fill, and would tell the speaker nothing. Saying no
    /// tells them exactly what the rule is, and a player who was not trying to break it rewords.
    /// </remarks>
    public bool RefusedForLanguage(string text)
    {
        var filter = Options?.WordFilter ?? WordFilter.None;

        if (!filter.Matches(text, out _))
        {
            return false;
        }

        Reply("That word is not allowed here.", "bad");
        return true;
    }

    public void Reply(string text) => Actor.SendText(text);

    public void Reply(string text, string style) => Actor.SendText(text, style);

    /// <summary>Sends to everyone else in the actor's room.</summary>
    /// <summary>
    /// Tells the rest of the room something they can only have <em>seen</em>, skipping sleepers.
    /// </summary>
    /// <remarks>
    /// The default for anything a character does with their body - walking in or out, taking
    /// something off the floor, putting a helmet on. <see cref="Broadcast"/> stays the right call
    /// for speech, which is the one thing in a room that reaches somebody with their eyes shut.
    /// </remarks>
    /// <param name="speech">
    /// True for something the actor <em>said</em> - speech, an emote - which a listener may have
    /// chosen not to hear (see <see cref="PlayerActor.Ignores"/>). False, the default, for
    /// narration of what happened in the room, which everybody sees regardless: an ignore is
    /// about what is said to you, not about pretending somebody is not there.
    /// </param>
    public void BroadcastSight(string text, string? style = null, bool speech = false)
    {
        var message = Line(text, style);

        foreach (var other in World.OthersAwakeIn(Actor.RoomKey, Actor))
        {
            if (speech && other.Ignores(Actor))
            {
                continue;
            }

            other.Send(message);
        }
    }

    /// <inheritdoc cref="BroadcastSight"/>
    public void Broadcast(string text, string? style = null, bool speech = false)
    {
        var message = Line(text, style);

        foreach (var other in World.OthersIn(Actor.RoomKey, Actor))
        {
            if (speech && other.Ignores(Actor))
            {
                continue;
            }

            other.Send(message);
        }
    }

    /// <summary>
    /// One sentence, built once, for everybody who is about to be told it.
    /// </summary>
    /// <remarks>
    /// <b>The event is shared, not copied per recipient, and that is safe by construction.</b>
    /// <c>OutboundEvent</c> and every payload under it are immutable records, and a session does
    /// nothing to what it is handed but serialise it — so one instance can sit in every recipient's
    /// channel at once.
    ///
    /// Calling <c>SendText</c> per recipient allocated four objects apiece — the event, the
    /// payload, the span and the array holding it — for a sentence identical for all of them. A
    /// movement broadcasts twice, so in a room holding sixty people that was some five hundred
    /// objects to say somebody walked in, and the allocation rate showed up in the pulse histogram
    /// as a long tail rather than as a steady slowdown (PLAN.md §11).
    /// </remarks>
    private static OutboundEvent Line(string text, string? style) =>
        new(EventTypes.Text, style is null ? TextPayload.Plain(text) : TextPayload.Styled(text, style));
}
