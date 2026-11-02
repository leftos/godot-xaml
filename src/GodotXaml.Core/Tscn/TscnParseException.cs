namespace GodotXaml.Core.Tscn;

/// <summary>
/// A .tscn or .tres text Godot's loader rejects; the reason is Godot's own error text where Godot has one
/// (scene/resources/resource_format_text.cpp, core/variant/variant_parser.cpp).
/// </summary>
/// <param name="reason">The error text, without the position.</param>
/// <param name="line">The 1-based line of the error.</param>
/// <param name="column">The 1-based column of the error.</param>
/// <param name="inner">The value parser's exception this one reports, or null.</param>
public sealed class TscnParseException(string reason, int line, int column, Exception? inner)
    : Exception($"{reason} (line {line}, column {column})", inner)
{
    /// <summary>The error text, without the position.</summary>
    public string Reason { get; } = reason;

    /// <summary>The 1-based line of the error.</summary>
    public int Line { get; } = line;

    /// <summary>The 1-based column of the error.</summary>
    public int Column { get; } = column;
}
