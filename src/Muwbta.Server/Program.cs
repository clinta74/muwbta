using System.Text.Json;
using System.Text.Json.Serialization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Moderation;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Systems;
using Muwbta.Persistence;
using Muwbta.Persistence.Converters;
using Muwbta.Persistence.Seeding;
using Muwbta.Server;
using Muwbta.Server.Admin;
using Muwbta.Server.Assist;
using Muwbta.Server.Auth;
using Muwbta.Server.Building;
using Muwbta.Server.Characters;
using Muwbta.Server.Game;
using Muwbta.Server.Infrastructure;
using Muwbta.Server.Moderation;
using Muwbta.Server.Infrastructure.Repositories;
using Muwbta.Server.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var connectionString = BuildConnectionString(builder.Configuration);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddMuwbtaPersistence(connectionString);

builder.Services.AddMuwbtaEngine(options =>
{
    options.StartingRoom = StarterWorldSeeder.StartingRoom;

    // The Engine section was never read at all. docker-compose.prod.yml has set
    // Engine__LinkDeadGraceSeconds and Engine__StartingRoom since it was written, and both were
    // silently ignored — a knob that reads as configured and does nothing is worse than one that
    // does not exist, because nobody goes looking for it when the value appears not to apply.
    //
    // Read key by key rather than `.Bind(options)`, for two reasons that EngineConfigurationBinding
    // tests pin down, because both are the sort that change quietly under a dependency upgrade:
    //
    //  - Bind cannot set StartingRoom at all. RoomKey is a readonly record struct with no type
    //    converter, so the binder leaves the property untouched rather than reporting that it
    //    could not convert — which would have left Engine__StartingRoom exactly as it was, a
    //    setting that reads as configured and does nothing.
    //  - Bind throws on a scalar it cannot convert, so a typo in the grace window takes the host
    //    down at startup. Fail-fast is right for a value with no default; this one has a correct
    //    default already, and refusing to boot over it trades a cosmetic mistake for an outage.
    var engine = builder.Configuration.GetSection("Engine");

    if (int.TryParse(engine["LinkDeadGraceSeconds"], out var graceSeconds) && graceSeconds > 0)
    {
        options.LinkDeadGraceSeconds = graceSeconds;
    }

    if (RoomKey.TryParse(engine["StartingRoom"], out var startingRoom))
    {
        options.StartingRoom = startingRoom;
    }
});

// The Engine does not reference EF Core, so the Server supplies adapters.
builder.Services.AddSingleton<IWorldSource, EfWorldSource>();
builder.Services.AddSingleton<IMobTemplateRepository, EfMobTemplateRepository>();
builder.Services.AddSingleton<IItemTemplateRepository, EfItemTemplateRepository>();
builder.Services.AddSingleton<ISpawnerRepository, EfSpawnerRepository>();
builder.Services.AddSingleton<IAbilityRepository, EfAbilityRepository>();
builder.Services.AddSingleton<IQuestRepository, EfQuestRepository>();
builder.Services.AddSingleton<ICharacterQuestRepository, EfCharacterQuestRepository>();

builder.Services.AddSingleton<EffectRegistry>();

// Lets an admin's `shutdown` reach the host. Registered here rather than in the Engine because
// stopping the process is the Server's business; the Engine only asks.
builder.Services.AddSingleton<IShutdownSignal, HostShutdownSignal>();

builder.Services.AddSingleton<CharacterSaveQueue>();
builder.Services.AddSingleton<ICharacterSaveQueue>(sp => sp.GetRequiredService<CharacterSaveQueue>());
builder.Services.AddHostedService<CharacterSaveWorker>();

builder.Services.AddSingleton<ItemSaveQueue>();
builder.Services.AddSingleton<IItemSaveQueue>(sp => sp.GetRequiredService<ItemSaveQueue>());
builder.Services.AddHostedService<ItemSaveQueueWorker>();

builder.Services.AddSingleton<CharacterQuestSaveQueue>();
builder.Services.AddSingleton<ICharacterQuestSaveQueue>(sp => sp.GetRequiredService<CharacterQuestSaveQueue>());
builder.Services.AddHostedService<CharacterQuestSaveWorker>();

builder.Services.AddSingleton<WorldWriteQueue>();
builder.Services.AddSingleton<IWorldWriteQueue>(sp => sp.GetRequiredService<WorldWriteQueue>());
builder.Services.AddHostedService<WorldWriteWorker>();

// Shutdown handler: ensures all pending saves and mutations are flushed before server stops
builder.Services.AddHostedService<ShutdownFlushService>();

// Account administration (PLAN.md §7.7). Same shape: the loop enqueues, a worker does the
// database work and sends the answer back through the loop.
builder.Services.AddSingleton<AccountAdminQueue>();
builder.Services.AddSingleton<IAccountAdminQueue>(sp => sp.GetRequiredService<AccountAdminQueue>());
builder.Services.AddHostedService<AccountAdminWorker>();
builder.Services.AddScoped<AccountAdminService>();

// Sessions are keyed by character, so one account can play several at once. The cap exists
// because each character holds an open SSE connection and a ring buffer.
var sessionOptions = new SessionRegistryOptions();
builder.Configuration.GetSection("Sessions").Bind(sessionOptions);
builder.Services.AddSingleton(sessionOptions);
builder.Services.AddSingleton<SessionRegistry>();

// The drawn maps, read out of the assembly once. Five files of about 150 KB, and they cannot
// change without a redeploy, so holding them costs less than decompressing one per request.
builder.Services.AddSingleton<MapSheets>();

// Reaps sessions whose client has stopped saying it is there. Without it, a client that vanishes
// without closing its socket is not noticed until the kernel gives up retransmitting - measured at
// over sixteen minutes, with or without nginx in front (PLAN.md §11).
builder.Services.AddHostedService<SessionLivenessMonitor>();

builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();

var authOptions = new AuthOptions();
builder.Configuration.GetSection("Auth").Bind(authOptions);
builder.Services.AddSingleton(authOptions);

// The per-account sign-in backoff (LoginThrottle). In memory and single-instance, like the rate
// limiter and for the same reason.
builder.Services.AddSingleton<LoginThrottle>();

// The server's security counters (ServerMetrics), registered the way EngineMetrics is: through
// the meter factory when there is one, so the exporter's listener finds them.
builder.Services.AddSingleton<ServerMetrics>(sp =>
    new ServerMetrics(sp.GetService<System.Diagnostics.Metrics.IMeterFactory>()));

// Two credentials, one policy scheme to choose between them (docs/PAT-AND-MCP.md §6). The cookie
// is unchanged and stays the browser's; a request carrying an Authorization header is a program
// holding a personal access token. Selecting on the header rather than on the path means every
// existing RequireAuthorization(...) keeps working untouched, and HttpContext.TryGetAccountId
// works for both because both emit NameIdentifier.
//
// The cookie remains the default for anything with neither, which includes the SSE endpoints the
// browser's EventSource cannot put a header on.
builder.Services
    .AddAuthentication(options => options.DefaultScheme = MuwbtaAuthentication.PolicyScheme)
    .AddPolicyScheme(MuwbtaAuthentication.PolicyScheme, "cookie or access token", options =>
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.Count > 0
                ? AccessTokenDefaults.Scheme
                : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, AccessTokenHandler>(AccessTokenDefaults.Scheme, null)
    .AddCookie(options =>
    {
        options.Cookie.Name = authOptions.CookieName;

        // PLAN.md §3.2: HttpOnly because the browser's native EventSource cannot send an
        // Authorization header, so the cookie is the only credential the stream can carry.
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Unconditional in Production. It used to follow the request scheme everywhere, on the
        // theory that production is HTTPS - but the request reaches Kestrel from nginx as plain
        // HTTP, so the flag was never set on any deployment with a proxy in front, which is every
        // deployment. Honouring forwarded headers (ProxyOptions) makes SameAsRequest correct when
        // the chain is configured right; Always means the cookie does not depend on that.
        //
        // Elsewhere it still follows the scheme: the dev client and the test host both talk
        // plain HTTP to localhost, and a Secure cookie the browser refuses to return over HTTP is
        // a sign-in that does not stick. The dev client proxies through Vite so the browser sees
        // one origin, which is what lets SameSite=Lax behave identically in both.
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = authOptions.SessionTimeout;
        options.SlidingExpiration = true;

        // This is an API, not a site with a login page. Without these the framework answers
        // an unauthenticated fetch with a 302 to /Account/Login, which reaches the client as
        // a confusing 200 containing HTML.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // Re-checks role and ban state against the database on an interval (PLAN.md §7.7).
        // Without it a demotion, or a ban on someone already connected, does nothing until the
        // cookie expires a fortnight later.
        options.Events.OnValidatePrincipal = PrincipalRevalidator.ValidateAsync;
    });

builder.Services.AddAuthorization(options => options.AddMuwbtaPolicies());

// Enums cross the wire as their names, not their ordinals. The general converter is what a
// bespoke converter per enum could not be - an ability's Path, cost type, and targeting mode all
// serialised as integers until it was added, so the Abilities tab filtered `path === 'Warden'`
// against a 0 and rendered an empty list.
//
// The set comes from BundleFormat, and the ordering rationale lives there with it. That is not
// tidiness: a bundle reaches the import endpoint through this pipeline and reaches tooling through
// a file read, and the two agreeing used to depend on nobody noticing they had drifted. Reading a
// bundle with anything less than this throws on the first path-restricted item in the Reaches.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    foreach (var converter in BundleFormat.Converters())
    {
        options.SerializerOptions.Converters.Add(converter);
    }
});

// The world builder (PLAN.md §7). Queries and writes are scoped because they own a DbContext;
// the throttle is a singleton because it is per-account state that must outlive a request.
builder.Services.AddScoped<BuilderQueries>();
builder.Services.AddScoped<WorldWriter>();
builder.Services.AddScoped<WorldEditor>();
builder.Services.AddScoped<WorldExporter>();
builder.Services.AddScoped<WorldImporter>();
builder.Services.AddSingleton<DigThrottle>();
builder.Services.AddSingleton<BuilderChangeFeed>();

// The builder AI assist (PLAN.md §13), and the first outbound HTTP this server has ever done.
// Off unless configured: a deployment with no model behind it must start and build exactly as it
// did before, so everything below is inside the flag and MapAssistEndpoints registers nothing.
var assistOptions = new AssistOptions();
builder.Configuration.GetSection(AssistOptions.Section).Bind(assistOptions);
builder.Services.Configure<AssistOptions>(builder.Configuration.GetSection(AssistOptions.Section));

if (assistOptions.Enabled)
{
    // A typed client through IHttpClientFactory, which is what keeps sockets from being exhausted
    // by a new HttpClient per call. The timeout is the handler's backstop only — the worker sets
    // its own per job, because "how long may one draft take" is a property of the work rather than
    // of the connection.
    builder.Services.AddHttpClient<IContentAssistant, OllamaContentAssistant>(http =>
    {
        http.BaseAddress = new Uri(assistOptions.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(assistOptions.TimeoutSeconds + 30);
    });

    builder.Services.AddScoped<IZoneContextSource, EfZoneContextSource>();
    builder.Services.AddScoped<IProseContextSource, EfProseContextSource>();
    builder.Services.AddSingleton<AssistQueue>();

    // Its own client with its own timeout: the warm-up runs for tens of minutes on modest
    // hardware, and the drafting client's handler timeout would abandon it partway for no reason.
    builder.Services.AddHttpClient(AssistWarmUp.ClientName, http =>
    {
        http.BaseAddress = new Uri(assistOptions.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(assistOptions.WarmUpTimeoutSeconds + 60);
    });

    // A singleton that is also a hosted service, because the worker asks it whether the model is
    // ready. AddHostedService<T> alone would construct a second, separate instance, and the worker
    // would then be waiting on an object nobody is warming.
    builder.Services.AddSingleton<AssistWarmUp>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AssistWarmUp>());
    builder.Services.AddHostedService<AssistWorker>();
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<MuwbtaDbContext>("database", tags: ["ready"]);

// Per-caller limits (§8, Phase 6). The numbers come from configuration so a deployment can adjust
// them without a rebuild — see RateLimiting.Options.
//
// The auth limit partitions by remote address. Behind the nginx front end that address is the
// proxy's for every caller unless the forwarded headers are honoured - which is what the Proxy
// section below is for. With nothing listed there, `RateLimits:AuthAttemptsPerMinute` is a
// site-wide cap rather than a per-visitor one.
builder.Services.AddMuwbtaRateLimiting(builder.Configuration);

// Which proxies may say who the caller really is (ProxyOptions). Nothing configured means nothing
// trusted, and the headers are ignored exactly as they were before this existed.
var proxyOptions = new ProxyOptions();
builder.Configuration.GetSection(ProxyOptions.Section).Bind(proxyOptions);
builder.Services.AddSingleton(proxyOptions);
builder.Services.Configure<ForwardedHeadersOptions>(proxyOptions.Apply);

// Ships EngineMetrics to Prometheus. All of the "where do the numbers go" decision is in
// MetricsExport; EngineMetrics itself still knows nothing about any of it.
builder.Services.AddMuwbtaMetricsExport();

var app = builder.Build();

// First, so that everything after it - the rate limiter's address partition, the cookie's Secure
// flag, anything that logs an address - sees the caller rather than the proxy.
app.UseForwardedHeaders();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, because the command and builder limits are partitioned by who is calling
// and would otherwise see every request as anonymous — which would put every player in one shared
// bucket and let any one of them exhaust it for the rest.
app.UseRateLimiter();

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------
app.MapAuthEndpoints();
app.MapAccessTokenEndpoints();
app.MapCharacterEndpoints();
app.MapGameEndpoints();
app.MapMapEndpoints();
app.MapBuilderEndpoints();
app.MapAssistEndpoints();
app.MapAdminEndpoints();

// What build this is. Beside the health checks because it is the same kind of route - an
// operator question, not a game one - and like them it takes no authentication.
app.MapVersionEndpoint();

// Liveness: is the process up? Deliberately runs no checks, so a database outage never
// causes an orchestrator to restart an otherwise healthy server.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness: can we actually serve? Includes the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync,
});

// Prometheus scrapes this. Mapped here beside the health checks because it is the same kind of
// thing — an operator route, not a game one — and like them it takes no authentication. That is
// safe only because nginx forwards /api/ and /health and nothing else; see MetricsExport.
app.MapPrometheusScrapingEndpoint();

// ---------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Muwbta.Server");
ServerLog.Starting(logger, app.Environment.EnvironmentName);

var csb = new NpgsqlConnectionStringBuilder(connectionString);
ServerLog.DatabaseConfigured(logger, csb.Host ?? "(unset)", csb.Database ?? "(unset)");

// Said at startup because it is the one setting whose absence looks like success: with nothing
// trusted the site still works, the rate limit still fires, and only the partition is wrong.
ServerLog.ProxiesTrusted(logger, proxyOptions.Describe());

// Said either way, because "off" and "broken" look identical from the browser: the endpoints are
// not registered when it is off, so the probe 404s and the Suggest buttons are absent, which is
// exactly what a failed deployment looks like too.
if (assistOptions.Enabled)
{
    AssistLog.Enabled(logger, assistOptions.Model, assistOptions.BaseUrl);
}
else
{
    AssistLog.Disabled(logger);
}

// Migrate on startup in every environment. The game loop is single-writer by design (§2.1)
// with no backplane to share world state, so this process cannot be scaled horizontally
// anyway - there is no second instance to race with. EF also takes an exclusive advisory
// lock for the duration, so even an accidental second instance waits rather than colliding.
// The tradeoff accepted here: a bad migration fails the deploy at startup rather than at a
// separate gate, so rollback is "deploy the previous image", not "stop the migration job".
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<MuwbtaDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    ServerLog.ApplyingMigrations(logger);

    // Zero disables the wait entirely, which is what the tests that point at an unreachable
    // database use so they still fail in a second rather than sixty.
    var retryBudget = TimeSpan.FromSeconds(
        app.Configuration.GetValue("Database:MigrationRetryBudgetSeconds", 60d));

    await StartupMigrator.RunAsync(
        db.Database.MigrateAsync,
        StartupMigrator.RetryPolicy.For(retryBudget),
        logger,
        TimeProvider.System);

    // Abilities are reconciled in every environment, unlike starter content. They are not a
    // fixture: the level table promises them, so a missing row is a character who cannot cast
    // what the game has already told them they know.
    var abilities = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);
    if (abilities.ChangedAnything)
    {
        ServerLog.AbilitiesReconciled(logger, abilities.Added, abilities.Updated, abilities.Removed);
    }

    // Validated on every boot, because the reconcile no longer guarantees the table's shape. A row
    // can now arrive from a builder, from an import, or from a migration backfill that did not name
    // it, and every way an ability can be wrong is a way that fails in silence: the cast succeeds,
    // the cost is spent, and nothing happens.
    //
    // Reported, never fatal. A single mistyped effect key must not stop a server from starting -
    // that trades one broken ability for a world nobody can reach, which is the worse of the two by
    // a wide margin, and it is the same argument §7.4 makes about a broken world.
    await ValidateAbilitiesAsync(db, app.Services, logger);

    // Seeding stays development-only: it writes starter content, which is a fixture, not schema.
    if (app.Environment.IsDevelopment())
    {
        if (await StarterWorldSeeder.SeedAsync(db))
        {
            ServerLog.SeededStarterWorld(logger, StarterWorldSeeder.StartingRoom.ToString());
        }
        else
        {
            ServerLog.SeedSkipped(logger);
        }

        // Outside the branch above, because that one skips a database that already has a world -
        // and every development database made before §4.16 has exactly that shape.
        if (await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db))
        {
            ServerLog.SeededStarterConfiguration(logger, StarterWorldSeeder.ConfigurationKey);
        }
    }

    // Every environment, like the abilities above and unlike starter content: a server that has
    // never been told what to refuse should not be the one that finds out the hard way. Only fills
    // a configuration that has none, so an operator who cleared the list keeps it cleared.
    if (await SeedBlockedWordsAsync(db))
    {
        ServerLog.BlockedWordsSeeded(logger);
    }

    // Read into the running filter whether it was just seeded or has been there for months. The
    // policy is one row for the whole server, so unlike the welcome message and the canon it is
    // not re-read when a configuration is activated - activating a different realm must not
    // change what the people playing here may say to each other.
    await LoadBlockedWordsAsync(db, app.Services);

    // After any seeding, for the same reason as the configuration below: a first boot should serve
    // the sheets the world it just planted came with. Failing to read them is not fatal - a server
    // that will not start because a drawing is unreadable has traded a map for a world.
    await LoadMapSheetsAsync(db, app.Services, logger);

    // Last, and after any seeding, so a first boot can point at a room that now exists. The
    // database is the authority for these two (§4.16); EngineOptions carries whatever came from
    // configuration until this overwrites it, and stays as-is when nothing is active.
    await LoadActiveConfigurationAsync(db, app.Services, logger);
}

/// <summary>
/// Copies the active starter configuration into the running <see cref="EngineOptions"/>.
/// </summary>
/// <remarks>
/// Read once at boot rather than per login, because the loop is the only thing that may change it
/// afterwards — a builder activating a different configuration arrives as a mutation, which is what
/// makes the swap take effect without a restart.
///
/// A stored room key that no longer parses is ignored rather than fatal, and loudly: the fallback
/// is a starting room that at least exists in code, where refusing to boot would take the whole
/// server down over one editable text field.
/// </remarks>
/// <summary>
/// Writes the shipped blocked-words list, once, if this server has never had one.
/// </summary>
/// <remarks>
/// Keyed on the row existing rather than on it being empty, which is the distinction that matters:
/// an operator who cleared the list on purpose has a row saying so, and a server that re-imposed a
/// filter somebody removed would be worse than one that never had it. Add-only, like the ability
/// reconcile, and for the same reason.
/// </remarks>
static async Task<bool> SeedBlockedWordsAsync(MuwbtaDbContext db)
{
    if (DefaultBlockedWords.List.Length == 0)
    {
        return false;
    }

    var policy = await db.ModerationPolicies
        .FirstOrDefaultAsync(p => p.Key == ModerationPolicy.SingletonKey);

    if (policy is not null)
    {
        return false;
    }

    db.ModerationPolicies.Add(new ModerationPolicy
    {
        Key = ModerationPolicy.SingletonKey,
        BlockedWords = DefaultBlockedWords.List,
    });

    await db.SaveChangesAsync();
    return true;
}

/// <summary>Compiles the stored word list into the running filter.</summary>
static async Task LoadBlockedWordsAsync(MuwbtaDbContext db, IServiceProvider services)
{
    var policy = await db.ModerationPolicies.AsNoTracking()
        .FirstOrDefaultAsync(p => p.Key == ModerationPolicy.SingletonKey);

    // Empty is a real value - "no filter" - so this is assigned either way rather than skipped
    // when there is no row, which is what an operator who cleared the list has.
    services.GetRequiredService<EngineOptions>().BlockedWords = policy?.BlockedWords ?? string.Empty;
}

/// <summary>
/// Hands the map service every sheet in the database. Called at startup and again after an import,
/// which is the only thing that can change them.
/// </summary>
static async Task LoadMapSheetsAsync(
    MuwbtaDbContext db,
    IServiceProvider services,
    ILogger logger)
{
    try
    {
        var sheets = await db.WorldMaps.AsNoTracking()
            .Select(m => new { m.WorldKey, m.Svg })
            .ToListAsync();

        services.GetRequiredService<MapSheets>().Load(sheets.Select(m => (m.WorldKey, m.Svg)));
        ServerLog.MapSheetsLoaded(logger, sheets.Count);
    }
    catch (Exception failure) when (failure is DbUpdateException or InvalidOperationException or NpgsqlException)
    {
        ServerLog.MapSheetsUnavailable(logger, failure);
    }
}

static async Task LoadActiveConfigurationAsync(
    MuwbtaDbContext db,
    IServiceProvider services,
    ILogger logger)
{
    var active = await db.GameConfigurations.AsNoTracking()
        .FirstOrDefaultAsync(c => c.IsActive);

    if (active is null)
    {
        return;
    }

    var options = services.GetRequiredService<EngineOptions>();

    if (RoomKey.TryParse(active.StartingRoomKey, out var startingRoom))
    {
        options.StartingRoom = startingRoom;
    }
    else if (!string.IsNullOrWhiteSpace(active.StartingRoomKey))
    {
        ServerLog.StoredStartingRoomUnusable(
            logger, active.StartingRoomKey, options.StartingRoom.ToString());
    }

    if (!string.IsNullOrWhiteSpace(active.WelcomeMessage))
    {
        options.WelcomeMessage = active.WelcomeMessage;
    }

    // Empty is a real value here too: "the built-in canon". Read before the assist warms up,
    // which is a hosted service and so starts after this.
    options.Canon = active.Canon;

    ServerLog.GameConfigurationLoaded(logger, active.Key, options.StartingRoom.ToString());
}

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Checks every ability row against <see cref="AbilityValidator"/> and reports what it finds.
/// </summary>
/// <remarks>
/// This is the load-time half of the validator, and it exists because the reconcile stopped being
/// authoritative: nothing else now guarantees that what is in the table can actually run. An
/// ability naming an effect that does not exist, or missing the one parameter its effect reads,
/// costs its resource and does nothing at all — so without a line in the log the first report is a
/// player saying a spell feels weak.
/// </remarks>
static async Task ValidateAbilitiesAsync(
    MuwbtaDbContext db,
    IServiceProvider services,
    ILogger logger)
{
    var rows = await db.Abilities.AsNoTracking().ToListAsync();
    var effects = services.GetRequiredService<EffectRegistry>();

    var problems = AbilityValidator.ValidateSet(rows, effects);
    var errors = 0;

    foreach (var problem in problems)
    {
        var key = string.IsNullOrEmpty(problem.Key) ? "progression" : problem.Key;

        if (problem.Severity == AbilityProblemSeverity.Error)
        {
            errors++;
            ServerLog.AbilityInvalid(logger, key, problem.Message);
        }
        else
        {
            ServerLog.AbilityWarning(logger, key, problem.Message);
        }
    }

    if (errors > 0)
    {
        ServerLog.AbilitiesInvalid(logger, errors);
    }
}

static string BuildConnectionString(IConfiguration config)
{
    // Try to build from individual connection parts first (docker-compose / container orchestration)
    var host = config["DatabaseConnection:Host"];
    var port = config["DatabaseConnection:Port"];
    var database = config["DatabaseConnection:Database"];
    var user = config["DatabaseConnection:User"];
    var password = config["DatabaseConnection:Password"];

    if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(database))
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = string.IsNullOrEmpty(port) ? 5432 : int.Parse(port),
            Database = database,
            Username = user ?? "postgres",
            Password = password,
            Pooling = true,
            MaxPoolSize = 20,
            ApplicationName = "muwbta-web",
            CommandTimeout = 30,
            Timezone = "UTC",
        };
        return builder.ConnectionString;
    }

    // Fallback to appsettings/user secrets connection string (development)
    var connectionString = config.GetConnectionString("Muwbta");
    if (!string.IsNullOrEmpty(connectionString))
    {
        return connectionString;
    }

    throw new InvalidOperationException(
        "Connection string not configured. Set either:\n" +
        "  1. DatabaseConnection:* environment variables (docker), or\n" +
        "  2. ConnectionStrings:Muwbta in appsettings.json / user secrets");
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        durationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            // Exception text is deliberately omitted: this endpoint is reachable from
            // outside and must not leak connection strings or stack traces.
            description = e.Value.Description,
        }),
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

/// <summary>
/// Exposed so WebApplicationFactory in Muwbta.Server.Tests can find the entry point.
/// </summary>
public partial class Program;
