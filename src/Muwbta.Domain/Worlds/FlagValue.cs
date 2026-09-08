namespace Muwbta.Domain.Worlds;

public enum FlagValueKind
{
    Boolean = 0,
    Number = 1,
    Text = 2,

    /// <summary>
    /// Raw JSON kept verbatim because it is none of the above - an array, an object, or null.
    /// Nothing in the registry produces one; it exists so "unknown keys are preserved" is
    /// literally true rather than true-except-for-shapes-we-did-not-anticipate.
    /// </summary>
    Json = 3,
}

/// <summary>
/// One value in a <see cref="FlagSet"/>. A tiny closed union rather than
/// <c>System.Text.Json.JsonElement</c> so Domain stays free of serialisation types, and rather
/// than <c>object</c> so "is this actually a boolean?" is a check the type system can answer.
/// </summary>
/// <remarks>
/// Non-boolean kinds exist today only so an unrecognised flag written by a newer binary
/// survives a load/save round trip intact (PLAN.md §4.10). Every flag in the registry is
/// currently <see cref="FlagValueKind.Boolean"/>.
/// </remarks>
public readonly record struct FlagValue
{
    private readonly bool _boolean;
    private readonly double _number;
    private readonly string? _text;

    private FlagValue(FlagValueKind kind, bool boolean, double number, string? text)
    {
        Kind = kind;
        _boolean = boolean;
        _number = number;
        _text = text;
    }

    public FlagValueKind Kind { get; }

    public static FlagValue Of(bool value) => new(FlagValueKind.Boolean, value, 0, null);

    public static FlagValue Of(double value) => new(FlagValueKind.Number, false, value, null);

    public static FlagValue Of(string value) =>
        new(FlagValueKind.Text, false, 0, value ?? string.Empty);

    /// <summary>
    /// The two primitives this union actually models, so a call site can pass one.
    /// </summary>
    /// <remarks>
    /// Implicit because the alternative is <c>FlagValue.Of(true)</c> at every point a flag is
    /// written, including a hundred tests that only want a peaceful room and have no opinion about
    /// how a flag is represented. There is no ambiguity to hide: a <c>bool</c> is the boolean kind
    /// and a <c>string</c> is the text kind, and no other type converts at all.
    /// </remarks>
    public static implicit operator FlagValue(bool value) => Of(value);

    /// <inheritdoc cref="op_Implicit(bool)"/>
    public static implicit operator FlagValue(string value) => Of(value);

    /// <summary>Wraps JSON text to be written back out untouched.</summary>
    public static FlagValue RawJson(string json) =>
        new(FlagValueKind.Json, false, 0, json ?? "null");

    /// <summary>
    /// False when the value is of another kind, which is how a wrong-typed flag
    /// (<c>"pvp": "yes"</c>) becomes "absent" and falls through to the registry default -
    /// always the safe value (PLAN.md §7.4).
    /// </summary>
    public bool TryAsBoolean(out bool value)
    {
        value = _boolean;
        return Kind == FlagValueKind.Boolean;
    }

    public bool TryAsNumber(out double value)
    {
        value = _number;
        return Kind == FlagValueKind.Number;
    }

    public bool TryAsText(out string value)
    {
        value = _text ?? string.Empty;
        return Kind == FlagValueKind.Text;
    }

    public bool TryAsRawJson(out string value)
    {
        value = _text ?? "null";
        return Kind == FlagValueKind.Json;
    }

    public override string ToString() => Kind switch
    {
        FlagValueKind.Boolean => _boolean ? "true" : "false",
        FlagValueKind.Number => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => _text ?? string.Empty,
    };
}
