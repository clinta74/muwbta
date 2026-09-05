#:project ../src/Muwbta.Server/Muwbta.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Writes the live configuration's canon back into docs/WORLD.md, above the canon:end marker.
//
//     dotnet run tools/sync-canon.cs
//     dotnet run tools/sync-canon.cs -- --key the-reaches --doc docs/WORLD.md
//
// The canon has two homes (PLAN.md §4.16). docs/WORLD.md is the reviewed, version-controlled one.
// The `canon` column on game_configurations is the live one: what the builder assist actually
// reads while that configuration is active, and what a builder edits from the configurations
// panel. The server embeds neither.
// This tool is how an edit made in the panel gets back into the repository - it reads the row and
// rewrites the file above the marker, leaving the authoring notes below it exactly as they were.
//
// It reads the database and writes a file. It never writes the database: the way in is the merge
// (tools/merge-bundles.cs --canon docs/WORLD.md --into the-reaches) followed by an import.

using Muwbta.Persistence;
using Muwbta.Server.Assist;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var docPath = "docs/WORLD.md";
string? connection = null;
string? key = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--doc":
            docPath = args[++i];
            break;
        case "--connection":
            connection = args[++i];
            break;
        case "--key":
            key = args[++i];
            break;
        case "-h" or "--help":
            Console.WriteLine("""
                Writes a configuration's canon into docs/WORLD.md above the canon:end marker.

                  --doc <file>           Default: docs/WORLD.md
                  --connection <string>  Npgsql connection string. Falls back to
                                         MUWBTA_CONNECTION, then to the compose defaults.
                  --key <key>            Which configuration. Default: the active one.
                """);
            return 0;
    }
}

connection ??= Environment.GetEnvironmentVariable("MUWBTA_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=muwbta;Username=muwbta;Password=password";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection);
dataSourceBuilder.EnableDynamicJson();

await using var dataSource = dataSourceBuilder.Build();

var options = new DbContextOptionsBuilder<MuwbtaDbContext>()
    .UseNpgsql(dataSource)
    .Options;

await using var db = new MuwbtaDbContext(options);

var configuration = key is null
    ? await db.GameConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.IsActive)
    : await db.GameConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key);

if (configuration is null)
{
    await Console.Error.WriteLineAsync(
        key is null ? "No configuration is active." : $"No configuration '{key}'.");
    return 1;
}

if (string.IsNullOrWhiteSpace(configuration.Canon))
{
    await Console.Error.WriteLineAsync(
        $"'{configuration.Key}' carries no canon, so there is nothing to write back. The way in "
        + "is the merge: dotnet run tools/merge-bundles.cs content -o build/the-reaches.json "
        + $"--canon docs/WORLD.md --into {configuration.Key}, then import.");
    return 1;
}

if (!File.Exists(docPath))
{
    await Console.Error.WriteLineAsync($"'{docPath}' does not exist.");
    return 1;
}

var document = File.ReadAllText(docPath).Replace("\r\n", "\n", StringComparison.Ordinal);
var marker = document.IndexOf(Canon.EndMarker, StringComparison.Ordinal);

if (marker < 0)
{
    await Console.Error.WriteLineAsync($"'{docPath}' has no '{Canon.EndMarker}' marker to write above.");
    return 1;
}

// The same normalisation the server applies, so the file and the row agree byte for byte and the
// canon test's estimate is measuring the text the model is given.
var canon = Canon.Resolve(configuration.Canon);
var rewritten = canon + "\n" + document[marker..];

File.WriteAllText(docPath, rewritten);

Console.WriteLine(
    $"Wrote {canon.Length:N0} characters (~{Canon.EstimateTokens(canon):N0} tokens) from "
    + $"'{configuration.Key}' into {docPath}.");
return 0;
