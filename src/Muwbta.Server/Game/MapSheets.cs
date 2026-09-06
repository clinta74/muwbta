using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Xml;
using Microsoft.Net.Http.Headers;

namespace Muwbta.Server.Game;

/// <summary>
/// The drawn map of each realm, as it left <c>tools/render-map.cs</c>.
/// </summary>
/// <param name="World">The world key the sheet draws, which is its file name.</param>
/// <param name="Title">The realm's name, read from the SVG's own <c>&lt;title&gt;</c>.</param>
/// <param name="Width">Intrinsic width in SVG user units, so a viewer can size a frame before it fetches.</param>
/// <param name="Height">Intrinsic height. These sheets are tall - Ossara is 1105 x 5520.</param>
public sealed record MapSheet(string World, string Title, int Width, int Height);

/// <summary>
/// Serves the rendered realm maps to any player who asks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Embedded rather than read from disk</b>, for the same reasons the builder assist's canon is
/// (<see cref="Assist.Canon"/>): a path in configuration is a path that can be missing in a
/// container, different between two servers, or edited under a running process. The maps are
/// derived entirely from authored content and regenerate byte-identically, so which map a build
/// serves is a property of that build - and a map that disagrees with the world it draws is
/// exactly the confusion a player cannot diagnose.
/// </para>
/// <para>
/// <b>Vector, not raster.</b> The tool writes a PNG beside each SVG and this serves only the SVG:
/// they are the same drawing, one is 150 KB and the other is 1.8 MB, and only one of them can be
/// zoomed into on a phone without turning to mush. The sheets are also long - five and a half
/// thousand user units for Ossara against eleven hundred across - so being able to scale is not a
/// nicety on a screen of any size.
/// </para>
/// <para>
/// Read once at startup and held. Five files of about 150 KB is nothing to hold, the bytes cannot
/// change without a redeploy, and it means a request costs a dictionary lookup rather than a
/// decompress.
/// </para>
/// </remarks>
/// <summary>
/// Serves the rendered realm maps to any player who asks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Content, not a build artefact.</b> These were embedded in the assembly, on the reasoning
/// that a build carries its own resources and a configured path can be missing in a container or
/// edited under a running process. All true, and it guarded the wrong failure. The rooms come from
/// the database by import; the drawing came from the build. Import a world without redeploying and
/// the map silently describes somewhere else - which is precisely the confusion a player cannot
/// diagnose, arriving by the one route embedding could not close. They travel in the bundle now
/// (<see cref="Building.BundleMap"/>) and arrive with the rooms they draw.
/// </para>
/// <para>
/// Still nothing on disk: this is handed its bytes and never opens a file, so the container that
/// publishes <c>/app/publish</c> and no <c>content/</c> beside it is unaffected either way.
/// </para>
/// <para>
/// <b>Vector, not raster.</b> The tool writes a PNG beside each SVG and only the SVG travels: the
/// same drawing at 150 KB against 1.8 MB, and only one of them can be zoomed into on a phone
/// without turning to mush. The sheets are also long - five and a half thousand user units for
/// Ossara against eleven hundred across - so scaling is not a nicety on a screen of any size.
/// </para>
/// <para>
/// Held in one immutable snapshot and swapped wholesale, so a request costs a dictionary lookup
/// and never sees a half-loaded set. <see cref="Load"/> is called at startup and again after an
/// import, which is the only thing that can change them.
/// </para>
/// </remarks>
public sealed class MapSheets
{
    private volatile Snapshot _current = Snapshot.Empty;

    /// <summary>Every sheet currently held, by world key.</summary>
    public IReadOnlyList<MapSheet> All => _current.All;

    /// <summary>
    /// Replaces every sheet with the ones given, atomically.
    /// </summary>
    /// <remarks>
    /// A sheet whose header cannot be read is skipped rather than thrown, and the rest are kept.
    /// Reading these used to happen once at startup where throwing was right - a wrong size lays
    /// out badly for every player, and failing loudly at boot beat serving it. It is content now,
    /// so the same throw would let one bad row taken from a bundle stop the server from starting,
    /// and <c>tools/check-bundle.cs</c> is the place to catch that instead.
    /// </remarks>
    public void Load(IEnumerable<(string World, string Svg)> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);

        var loaded = new Dictionary<string, Sheet>(StringComparer.OrdinalIgnoreCase);

        foreach (var (world, svg) in sheets)
        {
            var bytes = Encoding.UTF8.GetBytes(svg);

            string title;
            int width;
            int height;

            try
            {
                (title, width, height) = Describe(bytes, world);
            }
            catch (Exception failure) when (failure is XmlException or InvalidOperationException)
            {
                continue;
            }

            loaded[world] = new Sheet(
                new MapSheet(world, title, width, height),
                bytes,

                // Content-addressed, so the tag changes exactly when the drawing does and not when
                // the build does. A sheet is regenerated byte-identically from unchanged content,
                // so a deploy that did not touch the world does not re-download five megabytes of
                // map to every open client.
                new EntityTagHeaderValue(
                    Convert.ToHexStringLower(SHA256.HashData(bytes))[..32].Insert(0, "\"") + "\""));
        }

        _current = new Snapshot(
            loaded.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            [.. loaded.Values.Select(x => x.Describe).OrderBy(x => x.World, StringComparer.Ordinal)]);
    }

    public bool TryGet(string world, out byte[] svg, out EntityTagHeaderValue etag)
    {
        if (_current.ByWorld.TryGetValue(world, out var sheet))
        {
            (svg, etag) = (sheet.Svg, sheet.ETag);
            return true;
        }

        (svg, etag) = ([], new EntityTagHeaderValue("\"\""));
        return false;
    }

    /// <summary>
    /// Reads the title and the intrinsic size out of the sheet's own header.
    /// </summary>
    /// <remarks>
    /// With an <see cref="XmlReader"/> rather than a regular expression, and stopping at the
    /// title: these are 150 KB documents and only the first few hundred bytes are being asked
    /// about. A malformed sheet throws here, at startup, which is the right time - the alternative
    /// is a map that reports a plausible wrong size and lays out badly for every player.
    /// </remarks>
    private static (string Title, int Width, int Height) Describe(byte[] svg, string world)
    {
        using var stream = new MemoryStream(svg);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });

        var title = world;
        var width = 0;
        var height = 0;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Name == "svg")
            {
                width = Number(reader.GetAttribute("width"));
                height = Number(reader.GetAttribute("height"));
                continue;
            }

            if (reader.Name == "title")
            {
                title = reader.ReadElementContentAsString();
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"The embedded map '{world}.svg' has no usable width and height on its root element. "
                + "It is drawn by tools/render-map.cs, which always writes both.");
        }

        return (title, width, height);

        static int Number(string? value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private sealed record Sheet(MapSheet Describe, byte[] Svg, EntityTagHeaderValue ETag);

    private sealed record Snapshot(FrozenDictionary<string, Sheet> ByWorld, IReadOnlyList<MapSheet> All)
    {
        public static Snapshot Empty { get; } =
            new(FrozenDictionary<string, Sheet>.Empty, []);
    }
}
