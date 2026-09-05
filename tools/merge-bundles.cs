#:project ../src/Muwbta.Server/Muwbta.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Merges several WorldBundle files into one, so a whole world imports in a single upload.
//
//     dotnet run tools/merge-bundles.cs content -o build/the-reaches.json
//
// Takes files, directories, or both; a directory is searched recursively for *.json. Writes one
// bundle scoped {"kind": "all", "key": null} - the same shape a no-parameter
// `GET /api/builder/export` already produces, which is why this needs no server change and no
// format bump. WorldBundle.Worlds has always been a list.
//
// **A shim.** The merge itself is BundleMerge, in the server project, tested in the suite. This
// exists so it is one command away while authoring, and so the result can be checked before it goes
// anywhere near a server.
//
// Every file is read through BundleFormat, which is the same configuration the import endpoint
// binds with - so a file this merges is a file that endpoint can read, and the merged output is
// re-read here before it is written. That last step is the one a JSON-shuffling script could not
// do: it proves the thing on disk deserializes into the record the server expects, converters and
// all, rather than merely being well-formed JSON.
//
// **Gotcha, and it will cost you an hour.** `dotnet run` caches a file-based app's build against
// the *script's* content, so editing a referenced project does not invalidate it and neither does
// `dotnet build`. A shim can go on running library code from before your last change, silently and
// with a plausible answer. Touch this file to force a rebuild if a fix does not seem to have landed.
// The rules themselves run in `dotnet test`, which has no such cache - that is the authority, and
// this is the convenience.

using Muwbta.Server.Building;

var inputs = new List<string>();
string? output = null;
string? canonPath = null;
string? canonInto = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "-o" or "--out")
    {
        if (++i >= args.Length)
        {
            Console.Error.WriteLine("-o needs a path.");
            return 2;
        }

        output = args[i];
    }
    else if (args[i] == "--canon")
    {
        if (++i >= args.Length)
        {
            Console.Error.WriteLine("--canon needs a markdown file.");
            return 2;
        }

        canonPath = args[i];
    }
    else if (args[i] == "--into")
    {
        if (++i >= args.Length)
        {
            Console.Error.WriteLine("--into needs a configuration key.");
            return 2;
        }

        canonInto = args[i];
    }
    else
    {
        inputs.Add(args[i]);
    }
}

if (inputs.Count == 0 || output is null)
{
    Console.Error.WriteLine(
        "usage: dotnet run tools/merge-bundles.cs <file-or-directory> [...] -o <merged.json> "
        + "[--canon docs/WORLD.md --into <configuration-key>]");
    return 2;
}

var paths = new List<string>();

foreach (var input in inputs)
{
    if (Directory.Exists(input))
    {
        paths.AddRange(Directory.GetFiles(input, "*.json", SearchOption.AllDirectories));
    }
    else
    {
        paths.Add(input);
    }
}

// Sorted so a merge is reproducible whatever order the filesystem hands them back in.
paths.Sort(StringComparer.Ordinal);

if (paths.Count == 0)
{
    Console.WriteLine($"ERROR  nothing to merge in {string.Join(", ", inputs)}");
    return 1;
}

var sources = new List<BundleSource>();
var failed = false;

foreach (var path in paths)
{
    Console.WriteLine($"  read   {path}");

    if (!BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error))
    {
        Console.WriteLine($"  ERROR  {path} is not a bundle this build can read: {error}");
        failed = true;
        continue;
    }

    sources.Add(new BundleSource(path, bundle!));
}

if (failed)
{
    Console.WriteLine("FAILED");
    return 1;
}

var merged = BundleMerge.Merge(sources);

if (!merged.Ok)
{
    foreach (var error in merged.Errors)
    {
        Console.WriteLine($"  ERROR  {error}");
    }

    Console.WriteLine("FAILED");
    return 1;
}

var bundleToWrite = merged.Bundle!;

// The canon lives in a reviewed markdown file, not in the per-realm content files (a forty-
// kilobyte string nobody could review, six times over) and not in the server (an embedded copy
// that was the Reaches' whether or not the server was). It is written into the configuration
// here, on the way to the import, cut and normalised as the assist would cut it.
if (canonPath is not null)
{
    if (!File.Exists(canonPath))
    {
        Console.WriteLine($"  ERROR  --canon: '{canonPath}' does not exist");
        Console.WriteLine("FAILED");
        return 1;
    }

    var (withCanon, canonError) = BundleMerge.WithCanon(bundleToWrite, canonInto, File.ReadAllText(canonPath));

    if (withCanon is null)
    {
        Console.WriteLine($"  ERROR  --canon: {canonError}");
        Console.WriteLine("FAILED");
        return 1;
    }

    bundleToWrite = withCanon;
    var written = bundleToWrite.Configurations.Single(c => canonInto is null || c.Key == canonInto);
    Console.WriteLine(
        $"  canon  {canonPath} -> configuration {written.Key} "
        + $"(~{Muwbta.Server.Assist.Canon.EstimateTokens(written.Canon):N0} tokens)");
}

var json = BundleFormat.Write(bundleToWrite);

// Re-read what is about to be written. Cheap, and it is the difference between "the merge produced
// JSON" and "the merge produced a bundle the import endpoint will accept".
if (!BundleFormat.TryRead(json, out _, out var rereadError))
{
    Console.WriteLine($"  ERROR  the merged bundle does not read back: {rereadError}");
    Console.WriteLine("FAILED");
    return 1;
}

var directory = Path.GetDirectoryName(Path.GetFullPath(output));

if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

File.WriteAllText(output, json + Environment.NewLine);

var b = bundleToWrite;
Console.WriteLine($"  wrote  {output} (formatVersion {b.FormatVersion}, scope all)");
Console.WriteLine(
    $"         {b.Worlds.Count} worlds, {b.Zones.Count} zones, {b.ItemTemplates.Count} itemTemplates, "
    + $"{b.MobTemplates.Count} mobTemplates, {b.Rooms.Count} rooms, {b.Spawners.Count} spawners, "
    + $"{b.Quests.Count} quests, {b.Abilities.Count} abilities, "
    + $"{b.Configurations.Count} configurations");
Console.WriteLine("OK");
return 0;
