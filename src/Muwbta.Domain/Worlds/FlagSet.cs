namespace Muwbta.Domain.Worlds;

/// <summary>
/// The flag map stored on a world, zone, or room (PLAN.md §4.10). Deliberately an open map
/// rather than a bit field or a [Flags] enum: a new flag costs a registry entry, not a
/// migration.
/// </summary>
/// <remarks>
/// Keys the registry does not recognise are kept, never dropped. Rolling a server back must
/// not silently erase flags a newer binary wrote, and a builder who mistypes a key should see
/// it reported by /validate rather than watch it vanish on save.
/// </remarks>
public sealed class FlagSet
{
    private readonly Dictionary<string, FlagValue> _values;

    public FlagSet() => _values = new Dictionary<string, FlagValue>(StringComparer.Ordinal);

    public FlagSet(IEnumerable<KeyValuePair<string, FlagValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, FlagValue>(values, StringComparer.Ordinal);
    }

    public int Count => _values.Count;

    public IReadOnlyDictionary<string, FlagValue> Values => _values;

    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>Keys not in <see cref="RoomFlags"/> - reported by /validate (PLAN.md §7.4).</summary>
    public IEnumerable<string> UnknownKeys => _values.Keys.Where(k => !RoomFlags.IsKnown(k));

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>
    /// The boolean stored under this key, or null when absent <em>or stored as another kind</em>.
    /// Both cases mean "this level does not declare the flag", so resolution falls through.
    /// </summary>
    public bool? BooleanOrNull(string key) =>
        _values.TryGetValue(key, out var value) && value.TryAsBoolean(out var flag) ? flag : null;

    /// <summary>
    /// The text stored under this key, or null when absent <em>or stored as another kind</em>.
    /// The text sibling of <see cref="BooleanOrNull"/>, and null means the same thing: this level
    /// does not declare the flag, so resolution falls through.
    /// </summary>
    public string? TextOrNull(string key) =>
        _values.TryGetValue(key, out var value) && value.TryAsText(out var text) ? text : null;

    public void Set(string key, FlagValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = value;
    }

    public void Set(string key, bool value) => Set(key, FlagValue.Of(value));

    public void Set(string key, string value) => Set(key, FlagValue.Of(value));

    /// <summary>
    /// Removes the key entirely rather than storing <c>false</c>. That difference is the whole
    /// point of the inheritance chain: an absent key falls through to the zone or world, while
    /// an explicit <c>false</c> overrides them.
    /// </summary>
    public bool Clear(string key) => _values.Remove(key);

    public FlagSet Clone() => new(_values);

    public bool ContentEquals(FlagSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other.Count != Count)
        {
            return false;
        }

        foreach (var (key, value) in _values)
        {
            if (!other._values.TryGetValue(key, out var theirs) || !value.Equals(theirs))
            {
                return false;
            }
        }

        return true;
    }
}
