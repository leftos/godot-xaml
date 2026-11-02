using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>Walks values as the saver's _find_resources does (scene/resources/resource_format_text.cpp:1717-1747).</summary>
internal static class TscnValues
{
    /// <summary>A value and every value inside it: elements, keys, entry values, object properties and container script types.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value first, then the values inside it, depth first.</returns>
    public static IEnumerable<GodotValue> SelfAndDescendants(GodotValue value)
    {
        Stack<GodotValue> pending = new([value]);
        while (pending.Count > 0)
        {
            GodotValue current = pending.Pop();
            yield return current;
            foreach (GodotValue child in Children(current).Reverse())
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Whether the saver writes format 4 for these values: a PackedByteArray over 64 bytes or any PackedVector4Array
    /// turns compat off (resource_format_text.cpp:1736-1744, 1810).
    /// </summary>
    /// <param name="values">Every value of the document.</param>
    /// <returns>True for format 4.</returns>
    public static bool NeedsFormat4(IEnumerable<GodotValue> values) => values.SelectMany(SelfAndDescendants).Any(IsFormat4Value);

    private static bool IsFormat4Value(GodotValue value) =>
        value is GodotPackedBytes { Items.Length: > 64 } or GodotPackedReals { Kind: GodotPackedKind.Vector4 };

    private static IEnumerable<GodotValue> Children(GodotValue value) =>
        value switch
        {
            GodotArray array => ScriptOf(array.ElementType).Concat(array.Items),
            GodotDictionary dictionary => ScriptOf(dictionary.KeyType)
                .Concat(ScriptOf(dictionary.ValueType))
                .Concat(dictionary.Entries.SelectMany(entry => new[] { entry.Key, entry.Value })),
            GodotObject obj => obj.Properties.Select(property => property.Value),
            _ => [],
        };

    private static IEnumerable<GodotValue> ScriptOf(GodotTypeRef? type) => type?.Script is { } script ? [script] : [];
}
