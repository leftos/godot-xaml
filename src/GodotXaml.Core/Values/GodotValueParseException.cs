namespace GodotXaml.Core.Values;

/// <summary>A value text Godot's parser rejects; the reason is Godot's own error text (core/variant/variant_parser.cpp).</summary>
/// <param name="reason">Godot's error text.</param>
/// <param name="line">The 1-based line of the error.</param>
/// <param name="column">The 1-based column of the error.</param>
public sealed class GodotValueParseException(string reason, int line, int column) : Exception($"{reason} (line {line}, column {column})")
{
    /// <summary>Godot's error text, without the position.</summary>
    public string Reason { get; } = reason;

    /// <summary>The 1-based line of the error.</summary>
    public int Line { get; } = line;

    /// <summary>The 1-based column of the error.</summary>
    public int Column { get; } = column;
}
