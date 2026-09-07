namespace Muwbta.Domain.Weather;

/// <summary>
/// Smooth, repeatable noise over game hours — the thing that makes derived weather wander instead
/// of jump (docs/WEATHER.md §3).
/// </summary>
/// <remarks>
/// <para>
/// Value noise, not a random sequence: a hash gives every whole cell a fixed value, and any hour
/// between two cells is a smoothstep blend of them. That is what buys the property the whole
/// design rests on — asking twice about the same hour gives the same answer forever, and asking
/// about two nearby hours gives two nearby answers.
/// </para>
/// <para>
/// Three octaves, because one is not enough and four is not different. The long one is the season
/// within the season; the middle one is the front; the short one is the hour-to-hour texture that
/// stops a whole afternoon reading as one flat number.
/// </para>
/// </remarks>
internal static class WeatherNoise
{
    /// <summary>Roughly two game days: the slow wander behind everything else.</summary>
    private const double LongPeriodHours = 48;

    /// <summary>Fourteen game hours: a weather front arriving and leaving.</summary>
    private const double MediumPeriodHours = 14;

    /// <summary>Five game hours: enough texture that an afternoon is not one number.</summary>
    private const double ShortPeriodHours = 5;

    /// <summary>The three octaves together, in 0..1. Weights sum to one, so the range holds.</summary>
    public static double Layered(int seed, double hours) =>
        (0.60 * At(seed, hours, LongPeriodHours))
        + (0.30 * At(seed ^ 0x68bc21eb, hours, MediumPeriodHours))
        + (0.10 * At(seed ^ 0x2545f491, hours, ShortPeriodHours));

    /// <summary>One octave: a single smooth wave every <paramref name="periodHours"/>, in 0..1.</summary>
    public static double At(int seed, double hours, double periodHours)
    {
        var t = hours / periodHours;
        var cell = (long)Math.Floor(t);
        var f = t - cell;

        // Smoothstep rather than a straight blend, so the joins between cells have no corner in
        // them. A corner is visible as a front that arrives at a constant rate and then stops.
        var eased = f * f * (3 - (2 * f));

        return (Cell(seed, cell) * (1 - eased)) + (Cell(seed, cell + 1) * eased);
    }

    /// <summary>
    /// A fixed value in 0..1 for one cell. Splitmix-style avalanche, so neighbouring cells and
    /// neighbouring seeds are unrelated — without that, two worlds one letter apart would have
    /// noticeably similar weather.
    /// </summary>
    private static double Cell(int seed, long cell)
    {
        unchecked
        {
            var h = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) ^ (ulong)cell;

            h ^= h >> 33;
            h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 33;
            h *= 0xC4CEB9FE1A85EC53UL;
            h ^= h >> 33;

            // Top 53 bits, which is exactly what a double can hold without rounding.
            return (h >> 11) * (1.0 / (1UL << 53));
        }
    }
}
