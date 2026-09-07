namespace Muwbta.Domain.Weather;

public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

public enum TimeOfDay
{
    Night = 0,
    Dawn = 1,
    Morning = 2,
    Afternoon = 3,
    Dusk = 4,
}

/// <summary>
/// What time it is in the world: a date, a season, an hour, and the continuous hour count the
/// weather is drawn from (docs/WEATHER.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never stored.</b> Nothing ticks this and nothing writes "what time it is"
/// anywhere — it is arithmetic on the wall clock and a fixed epoch, so a restart cannot lose or
/// repeat a day, two processes agree without talking, and a test can ask what midwinter looks
/// like without running the world for a fortnight. It is the same argument §2.1 makes about the
/// game loop: a clock you can only observe is a clock you cannot debug.
/// </para>
/// <para>
/// <b>Why these numbers.</b> Five real minutes to the game hour puts a season at exactly seven
/// real days and a year at exactly twenty-eight. That is the whole reason for the scale: a player
/// who comes back next week is reliably in a different season, and a builder who writes "the
/// festival is in Thaw" knows what that means on a wall calendar. Much faster and seasons stop
/// reading as seasons; much slower and most players never see two. A day falls out at two real
/// hours, so night is about forty real minutes — long enough to happen to you inside one sitting,
/// short enough not to be a thing you wait out.
/// </para>
/// <para>
/// The month is 28 days and nothing reads it yet. It is here because it makes the year divide
/// evenly into twelve and because it is the obvious place a moon would hang.
/// </para>
/// </remarks>
public readonly record struct GameInstant
{
    public const int RealSecondsPerGameHour = 300;
    public const int HoursPerDay = 24;
    public const int DaysPerMonth = 28;
    public const int MonthsPerSeason = 3;
    public const int SeasonsPerYear = 4;

    public const int DaysPerSeason = DaysPerMonth * MonthsPerSeason;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
    public const int HoursPerYear = DaysPerYear * HoursPerDay;

    /// <summary>
    /// Year zero, hour zero — the first moment of the first Spring.
    /// </summary>
    /// <remarks>
    /// A constant rather than a column, deliberately, and this is the one place to revisit if that
    /// stops being true. Weather is presentation with no mechanical effect, so there is nothing an
    /// operator gains by moving year zero that is worth a migration, a bundle field and a builder
    /// control. Making it configurable later is one nullable timestamp on
    /// <c>game_configurations</c> and one argument here; nothing else in this file changes.
    /// </remarks>
    public static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private GameInstant(double totalHours) => TotalHours = totalHours;

    /// <summary>
    /// Game hours since the epoch, fractional. The continuous quantity — everything else on this
    /// type is a reading off it, and the weather noise is a function of it.
    /// </summary>
    public double TotalHours { get; }

    /// <summary>Years since the epoch, starting at 0.</summary>
    public int Year => (int)Math.Floor(TotalHours / HoursPerYear);

    /// <summary>Where in the year we are, 0 at the first instant of Spring and 1 a year later.</summary>
    public double YearPhase
    {
        get
        {
            var phase = TotalHours / HoursPerYear;
            return phase - Math.Floor(phase);
        }
    }

    public Season Season => (Season)Math.Min(
        SeasonsPerYear - 1,
        (int)(YearPhase * SeasonsPerYear));

    /// <summary>The day within the season, 1-based, as a player would count it.</summary>
    public int DayOfSeason => (DayOfYear % DaysPerSeason) + 1;

    /// <summary>The day within the year, 0-based.</summary>
    public int DayOfYear => (int)Mod((long)Math.Floor(TotalHours / HoursPerDay), DaysPerYear);

    /// <summary>Whole hours since midnight, 0-23.</summary>
    public int HourOfDay => (int)Mod((long)Math.Floor(TotalHours), HoursPerDay);

    /// <summary>Where in the day we are, 0 at midnight and 1 at the next midnight.</summary>
    public double DayPhase
    {
        get
        {
            var phase = TotalHours / HoursPerDay;
            return phase - Math.Floor(phase);
        }
    }

    /// <summary>
    /// Which part of the day this is, with dawn and dusk moving through the year.
    /// </summary>
    /// <remarks>
    /// The seasonal shift is an hour and a half either side of the equinox hours, which is small
    /// enough to be arithmetic nobody has to think about and large enough that a midsummer evening
    /// is noticeably longer than a midwinter one. It is the cheapest way to make the season
    /// something a player feels rather than something the <c>sky</c> verb tells them.
    /// </remarks>
    public TimeOfDay TimeOfDay
    {
        get
        {
            // Positive in summer, negative in winter: the amount the sun runs early and sets late.
            var stretch = 1.5 * Math.Sin(2 * Math.PI * (YearPhase - 0.125));

            var hour = DayPhase * HoursPerDay;
            var dawn = 6.0 - stretch;
            var morning = dawn + 1.5;
            var dusk = 19.0 + stretch;
            var night = dusk + 1.5;

            if (hour < dawn || hour >= night)
            {
                return TimeOfDay.Night;
            }

            if (hour < morning)
            {
                return TimeOfDay.Dawn;
            }

            if (hour < 12)
            {
                return TimeOfDay.Morning;
            }

            return hour < dusk ? TimeOfDay.Afternoon : TimeOfDay.Dusk;
        }
    }

    /// <summary>Whether the sun is down, which is the only thing most callers want to know.</summary>
    public bool IsDark => TimeOfDay is TimeOfDay.Night;

    public static GameInstant From(DateTimeOffset utcNow) =>
        new((utcNow - Epoch).TotalSeconds / RealSecondsPerGameHour);

    /// <summary>Directly from a game-hour count, which is how tests reach a particular season.</summary>
    public static GameInstant FromGameHours(double totalHours) => new(totalHours);

    /// <summary>How long a real span lasts in game hours - for tests and for the docs' arithmetic.</summary>
    public static double GameHoursIn(TimeSpan real) => real.TotalSeconds / RealSecondsPerGameHour;

    public GameInstant PlusHours(double hours) => new(TotalHours + hours);

    /// <summary>
    /// Remainder that stays non-negative, which C#'s <c>%</c> does not.
    /// </summary>
    /// <remarks>
    /// Instants before the epoch are perfectly legal - a test clock starting at the Unix epoch is
    /// fifty-six years earlier than this one - and without this they produce a negative day of the
    /// season, which is the kind of thing that surfaces as one baffling line in a <c>sky</c> report
    /// long after anyone remembers why the clock started where it did.
    /// </remarks>
    private static long Mod(long value, long modulus) => ((value % modulus) + modulus) % modulus;
}
