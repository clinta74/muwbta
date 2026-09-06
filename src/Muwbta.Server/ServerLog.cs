namespace Muwbta.Server;

/// <summary>
/// PLAN.md §2.4: all log messages are declared as [LoggerMessage] source-generated methods,
/// never inline template strings.
/// </summary>
/// <remarks>
/// This is a performance requirement rather than a style rule. A conventional
/// _logger.LogDebug("...{A}...", a) allocates a params object[] and boxes value types at the
/// call site, before the provider decides the level is disabled. On the game loop at 4 pulses
/// per second across 200 sessions that is constant allocation pressure, and the resulting GC
/// pauses land directly on the 25 ms p99 pulse budget (PLAN.md §11). The generator emits an
/// IsEnabled check first and returns before doing any work.
/// </remarks>
internal static partial class ServerLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Muwbta server starting in {Environment}")]
    public static partial void Starting(ILogger logger, string environment);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Database connection configured for host {Host}, database {Database}")]
    public static partial void DatabaseConfigured(ILogger logger, string host, string database);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Readiness check failed: {Check} reported {Status}")]
    public static partial void ReadinessCheckFailed(ILogger logger, string check, string status);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Applying database migrations")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Warning,
        Message = "Database not reachable yet, retrying in {DelaySeconds:0.#}s ({SecondsLeft:0}s of budget left): {Reason}")]
    public static partial void WaitingForDatabase(
        ILogger logger, double delaySeconds, double secondsLeft, string reason);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "Database reachable after {AttemptCount} attempts over {ElapsedSeconds:0.#}s")]
    public static partial void DatabaseReachedAfterRetries(
        ILogger logger, int attemptCount, double elapsedSeconds);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Seeded the starter world; players begin at {StartingRoom}")]
    public static partial void SeededStarterWorld(ILogger logger, string startingRoom);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "World already present, skipping seed")]
    public static partial void SeedSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "SSE stream closed for {Character}")]
    public static partial void StreamClosed(ILogger logger, string character);

    [LoggerMessage(
        EventId = 1034,
        Level = LogLevel.Information,
        Message = "Loaded {Count} realm map sheet(s)")]
    public static partial void MapSheetsLoaded(ILogger logger, int count);

    [LoggerMessage(
        EventId = 1035,
        Level = LogLevel.Warning,
        Message = "Could not read the realm map sheets; this server will serve no maps")]
    public static partial void MapSheetsUnavailable(ILogger logger, Exception failure);

    [LoggerMessage(
        EventId = 1029,
        Level = LogLevel.Information,
        Message = "Planted the starter game configuration '{Key}'")]
    public static partial void SeededStarterConfiguration(ILogger logger, string key);

    [LoggerMessage(
        EventId = 1027,
        Level = LogLevel.Information,
        Message = "Active game configuration is '{Key}'; players begin at {StartingRoom}")]
    public static partial void GameConfigurationLoaded(
        ILogger logger, string key, string startingRoom);

    [LoggerMessage(
        EventId = 1028,
        Level = LogLevel.Warning,
        Message = "The stored starting room '{Stored}' is not a room key; falling back to {Fallback}. "
            + "Set a valid one in the builder under Settings.")]
    public static partial void StoredStartingRoomUnusable(
        ILogger logger, string stored, string fallback);

    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Information,
        Message = "SSE stream for {Character} displaced by a newer connection")]
    public static partial void StreamDisplaced(ILogger logger, string character);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Failed to save a batch of {Count} character(s); the worker continues")]
    public static partial void CharacterSaveFailed(ILogger logger, int count, Exception exception);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "{Kind} '{Key}' applied to the world but could not be persisted; reloading")]
    public static partial void MutationNotPersisted(
        ILogger logger, string kind, string key, Exception exception);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Critical,
        Message = "World reload was refused by the loop; memory is ahead of the database")]
    public static partial void ResyncFailed(ILogger logger);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Critical,
        Message = "World reload threw; memory is ahead of the database")]
    public static partial void ResyncThrew(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "First account '{Username}' promoted to Admin")]
    public static partial void FirstAccountPromoted(ILogger logger, string username);

    [LoggerMessage(
        EventId = 1033,
        Level = LogLevel.Information,
        Message = "Forwarded headers are trusted from: {Proxies}")]
    public static partial void ProxiesTrusted(ILogger logger, string proxies);

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Information,
        Message = "Account '{Username}' role changed from {Before} to {After}")]
    public static partial void RoleChanged(
        ILogger logger, string username, string before, string after);

    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Error,
        Message = "Admin request {Request} failed; the worker continues")]
    public static partial void AdminRequestFailed(ILogger logger, string request, Exception exception);

    [LoggerMessage(
        EventId = 1014,
        Level = LogLevel.Information,
        Message = "Server shutting down; flushing character and world mutation queues")]
    public static partial void ShutdownFlushingQueues(ILogger logger);

    [LoggerMessage(
        EventId = 1015,
        Level = LogLevel.Error,
        Message = "Failed to save a batch of item(s); the worker continues")]
    public static partial void ItemSaveQueueError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1016,
        Level = LogLevel.Debug,
        Message = "Saved {Count} item(s) to the database")]
    public static partial void ItemsSaved(ILogger logger, int count);

    [LoggerMessage(
        EventId = 1017,
        Level = LogLevel.Information,
        Message = "Item save queue worker stopped")]
    public static partial void ItemSaveQueueStopped(ILogger logger);

    [LoggerMessage(
        EventId = 1018,
        Level = LogLevel.Error,
        Message = "Failed to save a batch of quest state; the worker retries on the next batch")]
    public static partial void QuestSaveQueueError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1019,
        Level = LogLevel.Information,
        Message = "Reconciled abilities against the catalogue: {Added} added, {Updated} updated, {Removed} removed")]
    public static partial void AbilitiesReconciled(ILogger logger, int added, int updated, int removed);

    [LoggerMessage(
        EventId = 1024,
        Level = LogLevel.Error,
        Message = "Ability '{Key}' will not work as authored: {Problem}")]
    public static partial void AbilityInvalid(ILogger logger, string key, string problem);

    [LoggerMessage(
        EventId = 1025,
        Level = LogLevel.Warning,
        Message = "Ability content warning ({Key}): {Problem}")]
    public static partial void AbilityWarning(ILogger logger, string key, string problem);

    [LoggerMessage(
        EventId = 1026,
        Level = LogLevel.Error,
        Message = "{Count} ability rows would not work as authored. The game will run; those abilities will not.")]
    public static partial void AbilitiesInvalid(ILogger logger, int count);

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Information,
        Message = "Character '{Name}' was deleted by an administrator")]
    public static partial void CharacterDeleted(ILogger logger, string name);

    [LoggerMessage(
        EventId = 1030,
        Level = LogLevel.Information,
        Message = "'{Character}' stopped answering {QuietSeconds:0}s ago; treating as link-dead")]
    public static partial void SessionWentQuiet(ILogger logger, string character, double quietSeconds);

    [LoggerMessage(
        EventId = 1031,
        Level = LogLevel.Warning,
        Message = "Session liveness sweeping is disabled. A client that vanishes without closing "
            + "its socket will hold its session until the kernel gives up retransmitting, which "
            + "was measured at over sixteen minutes.")]
    public static partial void LivenessSweepDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1032,
        Level = LogLevel.Error,
        Message = "A session liveness sweep failed. The sweeper is still running.")]
    public static partial void LivenessSweepFailed(ILogger logger, Exception exception);
}
