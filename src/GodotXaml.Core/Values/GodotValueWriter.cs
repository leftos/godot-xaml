using System.Collections.Immutable;
using System.Globalization;

namespace GodotXaml.Core.Values;

/// <summary>How values are written.</summary>
/// <param name="Compat">
/// Godot's p_compat: negative infinity written as <c>inf_neg</c> rather than <c>-inf</c>, and a PackedByteArray as a number list
/// rather than base64.
/// </param>
public sealed record ValueWriteOptions(bool Compat)
{
    /// <summary>The options var_to_str uses: compat on (variant_parser.h:162-163).</summary>
    public static ValueWriteOptions Default { get; } = new(Compat: true);
}

/// <summary>Writes values as Godot's text, a port of VariantWriter::write (core/variant/variant_parser.cpp:2012-2563).</summary>
public static class GodotValueWriter
{
    // variant_parser.cpp:2014-2556, one writer per value kind.
    private static readonly Dictionary<Type, Func<GodotValue, ValueWriteOptions, string>> Writers = new()
    {
        [typeof(GodotNull)] = (_, _) => "null",
        [typeof(GodotBool)] = (value, _) => ((GodotBool)value).Value ? "true" : "false",
        [typeof(GodotInt)] = (value, _) => Itos(((GodotInt)value).Value),
        [typeof(GodotFloat)] = (value, options) => GodotFloatFormat.FormatTopLevel(((GodotFloat)value).Value, options.Compat),
        [typeof(GodotString)] = (value, _) => "\"" + GodotStringEscape.CEscapeMultiline(((GodotString)value).Value) + "\"",
        [typeof(GodotStringName)] = (value, _) => "&\"" + GodotStringEscape.CEscape(((GodotStringName)value).Value) + "\"",
        [typeof(GodotNodePath)] = (value, _) => "NodePath(\"" + GodotStringEscape.CEscape(((GodotNodePath)value).Value) + "\")",
        [typeof(GodotReal)] = (value, options) => WriteReal((GodotReal)value, options),
        [typeof(GodotInts)] = (value, _) => WriteInts((GodotInts)value),
        [typeof(GodotRid)] = (value, _) => WriteRid((GodotRid)value),
        [typeof(GodotSignal)] = (_, _) => "Signal()",
        [typeof(GodotCallable)] = (_, _) => "Callable()",
        [typeof(GodotObject)] = (value, options) => WriteObject((GodotObject)value, options),
        [typeof(GodotResourceRef)] = (value, _) => WriteResourceRef((GodotResourceRef)value),
        [typeof(GodotPathRef)] = (value, _) => WritePathRef((GodotPathRef)value),
        [typeof(GodotArray)] = (value, options) => WriteArray((GodotArray)value, options),
        [typeof(GodotDictionary)] = (value, options) => WriteDictionary((GodotDictionary)value, options),
        [typeof(GodotPackedBytes)] = (value, options) => WritePackedBytes((GodotPackedBytes)value, options),
        [typeof(GodotPackedInts)] = (value, _) => WritePacked(GodotValueTables.PackedInts, ((GodotPackedInts)value).Items.Select(item => Itos(item))),
        [typeof(GodotPackedLongs)] = (value, _) => WritePacked(GodotValueTables.PackedLongs, ((GodotPackedLongs)value).Items.Select(Itos)),
        [typeof(GodotPackedFloats)] = (value, options) =>
            WritePacked(
                GodotValueTables.PackedFloats,
                ((GodotPackedFloats)value).Items.Select(item => GodotFloatFormat.Format(item, options.Compat))
            ),
        [typeof(GodotPackedDoubles)] = (value, options) =>
            WritePacked(
                GodotValueTables.PackedDoubles,
                ((GodotPackedDoubles)value).Items.Select(item => GodotFloatFormat.Format(item, options.Compat))
            ),
        [typeof(GodotPackedStrings)] = (value, _) =>
            WritePacked(
                GodotValueTables.PackedStrings,
                ((GodotPackedStrings)value).Items.Select(item => "\"" + GodotStringEscape.CEscape(item) + "\"")
            ),
        [typeof(GodotPackedReals)] = (value, options) => WritePackedReals((GodotPackedReals)value, options),
    };

    /// <summary>Godot's text for a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The text.</returns>
    public static string Write(GodotValue value, ValueWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        return Writers.TryGetValue(value.GetType(), out Func<GodotValue, ValueWriteOptions, string>? write)
            ? write(value, options)
            : throw new NotSupportedException($"GodotValueWriter has no writer for {value.GetType().Name}.");
    }

    // variant_parser.cpp:2039-2147: components with rtos_fix and no ".0", ", " between them.
    private static string WriteReal(GodotReal value, ValueWriteOptions options)
    {
        IEnumerable<string> components = value.Components.Select(component => GodotFloatFormat.Format(component, options.Compat));
        return GodotValueTables.Real(value.Kind).Name + "(" + string.Join(", ", components) + ")";
    }

    // variant_parser.cpp:2043-2070.
    private static string WriteInts(GodotInts value) =>
        GodotValueTables.Int(value.Kind).Name + "(" + string.Join(", ", value.Components.Select(component => Itos(component))) + ")";

    // variant_parser.cpp:2158-2165: itos of the id, which prints it as a signed 64-bit int.
    private static string WriteRid(GodotRid value) => value.Id is null ? "RID()" : "RID(" + Itos(unchecked((long)value.Id.Value)) + ")";

    // variant_parser.cpp:2213-2235: the class, then "name":value pairs with no spaces, then ")" and a newline.
    private static string WriteObject(GodotObject value, ValueWriteOptions options)
    {
        IEnumerable<string> properties = value.Properties.Select(property =>
            "\"" + GodotStringEscape.CEscapeMultiline(property.Name) + "\":" + Write(property.Value, options)
        );
        return "Object(" + value.ClassName + "," + string.Join(",", properties) + ")\n";
    }

    // scene/resources/resource_format_text.cpp:1634-1640.
    private static string WriteResourceRef(GodotResourceRef value) =>
        (value.Kind == GodotRefKind.External ? "ExtResource" : "SubResource") + "(\"" + GodotStringEscape.CEscapeMultiline(value.Id) + "\")";

    // variant_parser.cpp:2002-2010, encode_resource_reference: the uid first.
    private static string WritePathRef(GodotPathRef value)
    {
        IEnumerable<string> parts = new[] { value.Uid, value.Path }
            .OfType<string>()
            .Select(part => "\"" + GodotStringEscape.CEscapeMultiline(part) + "\"");
        return "Resource(" + string.Join(", ", parts) + ")";
    }

    // variant_parser.cpp:2337-2400: elements on one line, each written as a top-level value.
    private static string WriteArray(GodotArray value, ValueWriteOptions options)
    {
        string items = "[" + string.Join(", ", value.Items.Select(item => Write(item, options))) + "]";
        return value.ElementType is null ? items : "Array[" + WriteType(value.ElementType, options) + "](" + items + ")";
    }

    // variant_parser.cpp:2237-2335: an untyped side of a typed dictionary is Variant.
    private static string WriteDictionary(GodotDictionary value, ValueWriteOptions options)
    {
        string entries = WriteEntries(value.Entries, options);
        if (value.KeyType is null && value.ValueType is null)
        {
            return entries;
        }

        return "Dictionary[" + WriteSide(value.KeyType, options) + ", " + WriteSide(value.ValueType, options) + "](" + entries + ")";
    }

    // variant_parser.cpp:2307-2330: keys sorted, one entry per line, "{}" when empty.
    private static string WriteEntries(ImmutableArray<GodotEntry> entries, ValueWriteOptions options)
    {
        if (entries.IsEmpty)
        {
            return "{}";
        }

        IEnumerable<string> lines = entries
            .OrderBy(entry => entry.Key, GodotKeyOrder.Instance)
            .Select(entry => Write(entry.Key, options) + ": " + Write(entry.Value, options));
        return "{\n" + string.Join(",\n", lines) + "\n}";
    }

    private static string WriteSide(GodotTypeRef? type, ValueWriteOptions options) => type is null ? "Variant" : WriteType(type, options);

    private static string WriteType(GodotTypeRef type, ValueWriteOptions options) => type.Name ?? Write(type.Script!, options);

    // variant_parser.cpp:2402-2415: a number list with compat on; base64 with it off, nothing for an empty array.
    private static string WritePackedBytes(GodotPackedBytes value, ValueWriteOptions options)
    {
        if (options.Compat)
        {
            return WritePacked(GodotValueTables.PackedBytes, value.Items.Select(item => Itos(item)));
        }

        return value.Items.IsEmpty ? "PackedByteArray()" : "PackedByteArray(\"" + Convert.ToBase64String(value.Items.AsSpan()) + "\")";
    }

    // variant_parser.cpp:2493-2556: the components flat, with rtos_fix and no ".0".
    private static string WritePackedReals(GodotPackedReals value, ValueWriteOptions options) =>
        WritePacked(GodotValueTables.Packed(value.Kind), value.Components.Select(component => GodotFloatFormat.Format(component, options.Compat)));

    private static string WritePacked(ConstructorSpec spec, IEnumerable<string> items) => spec.Name + "(" + string.Join(", ", items) + ")";

    private static string Itos(long value) => value.ToString(CultureInfo.InvariantCulture);
}
