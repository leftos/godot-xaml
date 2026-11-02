using System.Collections.Immutable;
using System.Globalization;
using System.Text;

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
    // variant_parser.cpp:2014-2556, one writer per value kind. Each appends to the one builder the public Write made, as
    // VariantWriter::write streams into p_store_string_func, so a level of nesting costs no copy of the text below it.
    private static readonly Dictionary<Type, Action<GodotValue, ValueWriteOptions, StringBuilder>> Writers = new()
    {
        [typeof(GodotNull)] = (_, _, text) => text.Append("null"),
        [typeof(GodotBool)] = (value, _, text) => text.Append(((GodotBool)value).Value ? "true" : "false"),
        [typeof(GodotInt)] = (value, _, text) => text.Append(Itos(((GodotInt)value).Value)),
        [typeof(GodotFloat)] = (value, options, text) => text.Append(GodotFloatFormat.FormatTopLevel(((GodotFloat)value).Value, options.Compat)),
        [typeof(GodotString)] = (value, _, text) =>
            text.Append('"').Append(GodotStringEscape.CEscapeMultiline(((GodotString)value).Value)).Append('"'),
        [typeof(GodotStringName)] = (value, _, text) =>
            text.Append("&\"").Append(GodotStringEscape.CEscape(((GodotStringName)value).Value)).Append('"'),
        [typeof(GodotNodePath)] = (value, _, text) =>
            text.Append("NodePath(\"").Append(GodotStringEscape.CEscape(((GodotNodePath)value).Path)).Append("\")"),
        [typeof(GodotReal)] = (value, options, text) => WriteReal((GodotReal)value, options, text),
        [typeof(GodotInts)] = (value, _, text) => WriteInts((GodotInts)value, text),
        [typeof(GodotRid)] = (value, _, text) => WriteRid((GodotRid)value, text),
        [typeof(GodotSignal)] = (_, _, text) => text.Append("Signal()"),
        [typeof(GodotCallable)] = (_, _, text) => text.Append("Callable()"),
        [typeof(GodotObject)] = (value, options, text) => WriteObject((GodotObject)value, options, text),
        [typeof(GodotResourceRef)] = (value, _, text) => WriteResourceRef((GodotResourceRef)value, text),
        [typeof(GodotPathRef)] = (value, _, text) => WritePathRef((GodotPathRef)value, text),
        [typeof(GodotArray)] = (value, options, text) => WriteArray((GodotArray)value, options, text),
        [typeof(GodotDictionary)] = (value, options, text) => WriteDictionary((GodotDictionary)value, options, text),
        [typeof(GodotPackedBytes)] = (value, options, text) => WritePackedBytes((GodotPackedBytes)value, options, text),
        [typeof(GodotPackedInts)] = (value, _, text) =>
            WritePacked(GodotValueTables.PackedInts, ((GodotPackedInts)value).Items.Select(item => Itos(item)), text),
        [typeof(GodotPackedLongs)] = (value, _, text) =>
            WritePacked(GodotValueTables.PackedLongs, ((GodotPackedLongs)value).Items.Select(Itos), text),
        [typeof(GodotPackedFloats)] = (value, options, text) =>
            WritePacked(
                GodotValueTables.PackedFloats,
                ((GodotPackedFloats)value).Items.Select(item => GodotFloatFormat.Format(item, options.Compat)),
                text
            ),
        [typeof(GodotPackedDoubles)] = (value, options, text) =>
            WritePacked(
                GodotValueTables.PackedDoubles,
                ((GodotPackedDoubles)value).Items.Select(item => GodotFloatFormat.Format(item, options.Compat)),
                text
            ),
        [typeof(GodotPackedStrings)] = (value, _, text) =>
            WritePacked(
                GodotValueTables.PackedStrings,
                ((GodotPackedStrings)value).Items.Select(item => "\"" + GodotStringEscape.CEscape(item) + "\""),
                text
            ),
        [typeof(GodotPackedReals)] = (value, options, text) => WritePackedReals((GodotPackedReals)value, options, text),
    };

    /// <summary>Godot's text for a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The text.</returns>
    public static string Write(GodotValue value, ValueWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        var text = new StringBuilder();
        WriteValue(value, options, text);
        return text.ToString();
    }

    // One dispatch per value, each writer appending its own text and its children's to the same builder.
    private static void WriteValue(GodotValue value, ValueWriteOptions options, StringBuilder text)
    {
        if (!Writers.TryGetValue(value.GetType(), out Action<GodotValue, ValueWriteOptions, StringBuilder>? write))
        {
            throw new NotSupportedException($"GodotValueWriter has no writer for {value.GetType().Name}.");
        }

        write(value, options, text);
    }

    // variant_parser.cpp:2039-2147: components with rtos_fix and no ".0", ", " between them.
    private static void WriteReal(GodotReal value, ValueWriteOptions options, StringBuilder text)
    {
        text.Append(GodotValueTables.Real(value.Kind).Name).Append('(');
        AppendItems(text, value.Components.Select(component => GodotFloatFormat.Format(component, options.Compat)), ", ");
        text.Append(')');
    }

    // variant_parser.cpp:2043-2070.
    private static void WriteInts(GodotInts value, StringBuilder text)
    {
        text.Append(GodotValueTables.Int(value.Kind).Name).Append('(');
        AppendItems(text, value.Components.Select(component => Itos(component)), ", ");
        text.Append(')');
    }

    // variant_parser.cpp:2158-2165: itos of the id, which prints it as a signed 64-bit int.
    private static void WriteRid(GodotRid value, StringBuilder text)
    {
        if (value.Id is null)
        {
            text.Append("RID()");
            return;
        }

        text.Append("RID(").Append(Itos(unchecked((long)value.Id.Value))).Append(')');
    }

    // variant_parser.cpp:2213-2235: the class, then "name":value pairs with no spaces, then ")" and a newline.
    private static void WriteObject(GodotObject value, ValueWriteOptions options, StringBuilder text)
    {
        text.Append("Object(").Append(value.ClassName).Append(',');
        for (int index = 0; index < value.Properties.Length; index++)
        {
            if (index > 0)
            {
                text.Append(',');
            }

            GodotProperty property = value.Properties[index];
            text.Append('"').Append(GodotStringEscape.CEscapeMultiline(property.Name)).Append("\":");
            WriteValue(property.Value, options, text);
        }

        text.Append(")\n");
    }

    // scene/resources/resource_format_text.cpp:1634-1640.
    private static void WriteResourceRef(GodotResourceRef value, StringBuilder text) =>
        text.Append(value.Kind == GodotRefKind.External ? "ExtResource" : "SubResource")
            .Append("(\"")
            .Append(GodotStringEscape.CEscapeMultiline(value.Id))
            .Append("\")");

    // variant_parser.cpp:2002-2010, encode_resource_reference: the uid first.
    private static void WritePathRef(GodotPathRef value, StringBuilder text)
    {
        text.Append("Resource(");
        IEnumerable<string> parts = new[] { value.Uid, value.Path }
            .OfType<string>()
            .Select(part => "\"" + GodotStringEscape.CEscapeMultiline(part) + "\"");
        AppendItems(text, parts, ", ");
        text.Append(')');
    }

    // variant_parser.cpp:2337-2400: elements on one line, each written as a top-level value.
    private static void WriteArray(GodotArray value, ValueWriteOptions options, StringBuilder text)
    {
        if (value.ElementType is not null)
        {
            text.Append("Array[");
            WriteType(value.ElementType, options, text);
            text.Append("](");
        }

        text.Append('[');
        for (int index = 0; index < value.Items.Length; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            WriteValue(value.Items[index], options, text);
        }

        text.Append(']');
        if (value.ElementType is not null)
        {
            text.Append(')');
        }
    }

    // variant_parser.cpp:2237-2335: an untyped side of a typed dictionary is Variant.
    private static void WriteDictionary(GodotDictionary value, ValueWriteOptions options, StringBuilder text)
    {
        bool typed = value.KeyType is not null || value.ValueType is not null;
        if (typed)
        {
            text.Append("Dictionary[");
            WriteSide(value.KeyType, options, text);
            text.Append(", ");
            WriteSide(value.ValueType, options, text);
            text.Append("](");
        }

        WriteEntries(value.Entries, options, text);
        if (typed)
        {
            text.Append(')');
        }
    }

    // variant_parser.cpp:2307-2330: keys sorted, one entry per line, "{}" when empty.
    private static void WriteEntries(ImmutableArray<GodotEntry> entries, ValueWriteOptions options, StringBuilder text)
    {
        if (entries.IsEmpty)
        {
            text.Append("{}");
            return;
        }

        text.Append("{\n");
        ImmutableArray<GodotEntry> sorted = [.. entries.OrderBy(entry => entry.Key, GodotKeyOrder.Instance)];
        for (int index = 0; index < sorted.Length; index++)
        {
            if (index > 0)
            {
                text.Append(",\n");
            }

            WriteValue(sorted[index].Key, options, text);
            text.Append(": ");
            WriteValue(sorted[index].Value, options, text);
        }

        text.Append("\n}");
    }

    private static void WriteSide(GodotTypeRef? type, ValueWriteOptions options, StringBuilder text)
    {
        if (type is null)
        {
            text.Append("Variant");
            return;
        }

        WriteType(type, options, text);
    }

    private static void WriteType(GodotTypeRef type, ValueWriteOptions options, StringBuilder text)
    {
        if (type.Name is not null)
        {
            text.Append(type.Name);
            return;
        }

        WriteValue(type.Script!, options, text);
    }

    // variant_parser.cpp:2402-2415: a number list with compat on; base64 with it off, nothing for an empty array.
    private static void WritePackedBytes(GodotPackedBytes value, ValueWriteOptions options, StringBuilder text)
    {
        if (options.Compat)
        {
            WritePacked(GodotValueTables.PackedBytes, value.Items.Select(item => Itos(item)), text);
            return;
        }

        if (value.Items.IsEmpty)
        {
            text.Append("PackedByteArray()");
            return;
        }

        text.Append("PackedByteArray(\"").Append(Convert.ToBase64String(value.Items.AsSpan())).Append("\")");
    }

    // variant_parser.cpp:2493-2556: the components flat, with rtos_fix and no ".0".
    private static void WritePackedReals(GodotPackedReals value, ValueWriteOptions options, StringBuilder text) =>
        WritePacked(
            GodotValueTables.Packed(value.Kind),
            value.Components.Select(component => GodotFloatFormat.Format(component, options.Compat)),
            text
        );

    private static void WritePacked(ConstructorSpec spec, IEnumerable<string> items, StringBuilder text)
    {
        text.Append(spec.Name).Append('(');
        AppendItems(text, items, ", ");
        text.Append(')');
    }

    // Godot's ", " between the elements of a comma list.
    private static void AppendItems(StringBuilder text, IEnumerable<string> items, string separator)
    {
        bool first = true;
        foreach (string item in items)
        {
            if (!first)
            {
                text.Append(separator);
            }

            first = false;
            text.Append(item);
        }
    }

    private static string Itos(long value) => value.ToString(CultureInfo.InvariantCulture);
}
