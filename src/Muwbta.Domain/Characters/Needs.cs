namespace Muwbta.Domain.Characters;

/// <summary>
/// How hunger and thirst work: how fast they arrive, what they cost, and what to call them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An upkeep, not a survival mechanic.</b> Neglecting food slows recovery and does nothing else —
/// it never damages, never blocks, and cannot kill. The point is to give food, drink and a trip back
/// to town a reason to exist, and a rule that can strand or starve a player achieves that by making
/// the game worse.
/// </para>
/// <para>
/// <b>Everything here is a judgement call, and none of it came from the balance harness.</b> That
/// tool measures fights; the right pace for hunger is a question about how long a session feels,
/// which no simulation this repo has can answer. The constants below are a starting point chosen to
/// be forgiving, and they are meant to be moved after play.
/// </para>
/// </remarks>
public static class Needs
{
    /// <summary>Fully starving or fully parched. The scale runs <c>0</c> to here.</summary>
    public static readonly int Worst = 100;

    /// <summary>Hours of play from a full belly to starving.</summary>
    /// <remarks>
    /// <b>Longer than a session, deliberately.</b> Eight hours means an ordinary evening never
    /// meets hunger at all, and only a long day or a character left standing in town does. The
    /// upkeep is meant to be a reason to keep bread in the pack, not a clock to play against.
    /// </remarks>
    public const int HoursToStarving = 8;

    /// <summary>Hours of play from watered to parched.</summary>
    /// <remarks>
    /// Sooner than hunger, which is what gives a waterskin a job distinct from a loaf — otherwise
    /// the two are one need with two item types.
    /// </remarks>
    public const int HoursToParched = 6;

    /// <summary>The least of its normal rate recovery can fall to, when a need is at its worst.</summary>
    /// <remarks>
    /// Two fifths, so an ignored need is a long wait rather than a stopped one. A floor of zero
    /// would make carrying rations mandatory instead of sensible, and would strand anyone who ran
    /// out somewhere with nothing to eat.
    /// </remarks>
    public const double SlowestRegenShare = 0.4;

    /// <summary>Where a need stops being background and starts being worth saying out loud.</summary>
    /// <remarks>
    /// Announced on the way past, in <c>NeedsSystem</c>. An upkeep the player cannot see is an
    /// upkeep that reads as a bug — they would only know something was wrong from a regen rate they
    /// have no way to inspect.
    /// </remarks>
    public static readonly int[] Thresholds = [40, 70, 90];

    /// <summary>
    /// What recovery is multiplied by, for a character in this state.
    /// </summary>
    /// <remarks>
    /// <b>The worse of the two decides it, rather than the sum.</b> So the answer is always "deal
    /// with whichever is worse", and letting both slide is not punished twice for one mistake — a
    /// player who is starving and parched is in the same hole as one who is merely starving, and
    /// fixes it the same way.
    /// </remarks>
    public static double RegenShare(int hunger, int thirst)
    {
        var worst = Math.Clamp(Math.Max(hunger, thirst), 0, Worst);

        return 1.0 - ((1.0 - SlowestRegenShare) * worst / Worst);
    }

    /// <summary>The word for how hungry this is, or null when there is nothing to say.</summary>
    public static string? DescribeHunger(int hunger) => Describe(hunger, "hungry", "very hungry", "starving");

    /// <summary>The word for how thirsty this is, or null when there is nothing to say.</summary>
    public static string? DescribeThirst(int thirst) => Describe(thirst, "thirsty", "very thirsty", "parched");

    /// <summary>Both needs in one phrase, or null when neither is worth saying.</summary>
    /// <remarks>
    /// <b>Joined rather than listed, and both named when both apply.</b> A character can be
    /// starving and merely thirsty at once, and the two words are not interchangeable — "hungry
    /// and parched" is the sentence, and picking the worse of the two would drop the half the
    /// player still has to answer separately. That the worse one alone decides recovery
    /// (<see cref="RegenShare"/>) is a rule about arithmetic, not about what to say.
    /// </remarks>
    public static string? Describe(int hunger, int thirst)
    {
        var belly = DescribeHunger(hunger);
        var throat = DescribeThirst(thirst);

        return belly is null ? throat
            : throat is null ? belly
            : $"{belly} and {throat}";
    }

    private static string? Describe(int value, string mild, string bad, string worst) =>
        value >= Thresholds[2] ? worst
        : value >= Thresholds[1] ? bad
        : value >= Thresholds[0] ? mild
        : null;

    /// <summary>Answers a need by this much, never past full and never below it.</summary>
    public static int Reduced(int current, int by) => Math.Clamp(current - Math.Max(0, by), 0, Worst);

    /// <summary>Lets a need grow, never past its worst.</summary>
    public static int Increased(int current, int by) => Math.Clamp(current + Math.Max(0, by), 0, Worst);
}
