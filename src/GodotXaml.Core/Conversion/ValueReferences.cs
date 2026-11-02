using GodotXaml.Core.Values;

namespace GodotXaml.Core.Conversion;

/// <summary>The resource references inside Godot value text: <see cref="GodotResourceRef"/> and <see cref="GodotPathRef"/>.</summary>
internal static class ValueReferences
{
    /// <summary>
    /// Calls <paramref name="visit"/> for each reference inside a value, the value itself included, in the order Godot's
    /// saver finds resources (resource_format_text.cpp:1717-1735, _find_resources): an array's typed script, then its
    /// items; a dictionary's key and value scripts, then each entry's key and value. An <c>Object(…)</c> is not entered:
    /// it is not a Resource, and the saver returns before reading its properties (resource_format_text.cpp:1655-1659).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="visit">Called with each reference.</param>
    public static void Visit(GodotValue value, Action<GodotValue> visit)
    {
        switch (value)
        {
            case GodotResourceRef or GodotPathRef:
                visit(value);
                break;
            case GodotArray array:
                VisitType(array.ElementType, visit);
                foreach (GodotValue item in array.Items)
                {
                    Visit(item, visit);
                }

                break;
            case GodotDictionary dictionary:
                VisitDictionary(dictionary, visit);
                break;
        }
    }

    /// <summary>
    /// A copy of a value with each reference <see cref="Visit"/> meets replaced by <paramref name="map"/>'s result; an
    /// <c>Object(…)</c> is kept as it stands.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="map">The replacement of a reference; a container's script type must map to a reference again.</param>
    /// <returns>The value with its references replaced.</returns>
    public static GodotValue Map(GodotValue value, Func<GodotValue, GodotValue> map) =>
        value switch
        {
            GodotResourceRef or GodotPathRef => map(value),
            GodotArray array => new GodotArray([.. array.Items.Select(item => Map(item, map))], MapType(array.ElementType, map)),
            GodotDictionary dictionary => new GodotDictionary(
                [.. dictionary.Entries.Select(entry => new GodotEntry(Map(entry.Key, map), Map(entry.Value, map)))],
                MapType(dictionary.KeyType, map),
                MapType(dictionary.ValueType, map)
            ),
            _ => value,
        };

    private static void VisitDictionary(GodotDictionary dictionary, Action<GodotValue> visit)
    {
        VisitType(dictionary.KeyType, visit);
        VisitType(dictionary.ValueType, visit);
        foreach (GodotEntry entry in dictionary.Entries)
        {
            Visit(entry.Key, visit);
            Visit(entry.Value, visit);
        }
    }

    private static void VisitType(GodotTypeRef? type, Action<GodotValue> visit)
    {
        if (type?.Script is { } script)
        {
            visit(script);
        }
    }

    private static GodotTypeRef? MapType(GodotTypeRef? type, Func<GodotValue, GodotValue> map) =>
        type?.Script is { } script ? new GodotTypeRef(map(script)) : type;
}
