using System.Collections.Immutable;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>
/// A tag's fields by name, the last of a repeated name winning as in _parse_tag (variant_parser.cpp:1869). Each
/// known field is taken by name; <see cref="Rest"/> gives the others as source text, in source order.
/// </summary>
/// <param name="tag">The tag.</param>
internal sealed class TscnTagFields(TscnTag tag)
{
    private readonly Dictionary<string, TscnTagField> _byName = tag
        .Fields.GroupBy(field => field.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>A field's value, or null when the tag has none.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null.</returns>
    public GodotValue? Value(string name)
    {
        _taken.Add(name);
        return _byName.TryGetValue(name, out TscnTagField? field) ? field.Value : null;
    }

    /// <summary>A string field, or null when the tag has none.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The text, or null.</returns>
    public string? String(string name) => Typed<GodotString>(name, "string")?.Value;

    /// <summary>A string field the tag must have.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="missing">Godot's error text for a tag without it.</param>
    /// <returns>The text.</returns>
    public string Required(string name, string missing) => String(name) ?? throw TscnScanner.Error(missing, tag.Line, tag.Column);

    /// <summary>
    /// A field Godot reads as a path or a name, whichever string-like value it holds: a String, a StringName or a
    /// NodePath (core/variant/variant.cpp:1539-1560, 1992-1998).
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <returns>The text, or null when the tag has none.</returns>
    public string? Text(string name) =>
        Value(name) switch
        {
            null => null,
            GodotString text => text.Value,
            GodotStringName name2 => name2.Value,
            GodotNodePath path => path.Path,
            _ => throw WrongType(name, "a string, a StringName or a NodePath"),
        };

    /// <summary>A field Godot reads as an int, converting as Variant::_to_int does (core/variant/variant.h:337-352).</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The number, or null when the tag has none.</returns>
    public int? ToInt(string name) =>
        Value(name) switch
        {
            null => null,
            GodotNull => 0,
            GodotBool flag => flag.Value ? 1 : 0,
            GodotInt number => unchecked((int)number.Value),
            GodotFloat number => (int)number.Value,
            GodotString text => StringToInt(text.Value),
            _ => 0,
        };

    /// <summary>An int field, or null when the tag has none.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null.</returns>
    public int? Int(string name) => Typed<GodotInt>(name, "int") is { } value ? unchecked((int)value.Value) : null;

    /// <summary>A PackedInt32Array field; empty when the tag has none.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The elements.</returns>
    public ImmutableArray<int> Ints(string name) => Typed<GodotPackedInts>(name, "PackedInt32Array")?.Items ?? [];

    /// <summary>The fields not taken by name, as source text in source order.</summary>
    /// <returns>The fields.</returns>
    public ImmutableArray<TscnField> Rest() =>
        [
            .. tag
                .Fields.Where(field => !_taken.Contains(field.Name) && _byName[field.Name] == field)
                .Select(field => new TscnField(field.Name, field.Text)),
        ];

    /// <summary>The error for a field whose value is not of the type it must have.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="expected">The type it must have.</param>
    /// <returns>The exception to throw.</returns>
    public TscnParseException WrongType(string name, string expected)
    {
        TscnTagField field = _byName[name];
        return TscnScanner.Error($"Expected {expected} for '{name}' in '{tag.Name}' tag", field.Line, field.Column);
    }

    // core/string/ustring.cpp:2265-2308: digits accumulate, a '-' before any digit flips the sign, anything else is skipped.
    private static int StringToInt(string text)
    {
        int stop = text.IndexOf('.', StringComparison.Ordinal);
        long value = 0;
        bool positive = true;
        for (int i = 0; i < (stop < 0 ? text.Length : stop); i++)
        {
            char c = text[i];
            if (c is >= '0' and <= '9')
            {
                value = (value * 10) + (c - '0');
            }
            else if (value == 0 && c == '-')
            {
                positive = !positive;
            }
        }

        return unchecked((int)(positive ? value : -value));
    }

    private T? Typed<T>(string name, string expected)
        where T : GodotValue =>
        Value(name) switch
        {
            null => null,
            T typed => typed,
            _ => throw WrongType(name, expected),
        };
}
