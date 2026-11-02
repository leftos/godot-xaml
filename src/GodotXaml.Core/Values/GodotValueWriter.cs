using System.Globalization;

namespace GodotXaml.Core.Values;

/// <summary>How values are written.</summary>
/// <param name="Compat">Godot's p_compat: negative infinity written as <c>inf_neg</c> rather than <c>-inf</c>.</param>
public sealed record ValueWriteOptions(bool Compat)
{
    /// <summary>The options var_to_str uses: compat on (variant_parser.h:162-163).</summary>
    public static ValueWriteOptions Default { get; } = new(Compat: true);
}

/// <summary>Writes values as Godot's text, a port of VariantWriter::write (core/variant/variant_parser.cpp:2012-2563).</summary>
public static class GodotValueWriter
{
    // variant_parser.cpp:2014-2173, one writer per value kind.
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

    private static string Itos(long value) => value.ToString(CultureInfo.InvariantCulture);
}
