using System.Collections.Immutable;

namespace GodotXaml.Core.Values;

/// <summary>A math constructor: its 4.7.2 name, the legacy names Godot still reads, and its argument count.</summary>
/// <param name="Name">The name Godot writes.</param>
/// <param name="Arity">The number of arguments (variant_parser.cpp:716-920 checks it after the parse).</param>
/// <param name="Aliases">The legacy names Godot reads as this constructor.</param>
internal sealed record ConstructorSpec(string Name, int Arity, ImmutableArray<string> Aliases);

/// <summary>The value grammar's constructor specs and token names, from core/variant/variant_parser.cpp.</summary>
internal static class GodotValueTables
{
    // variant_parser.cpp:709-920. Every real_t constructor takes float components (real_t is float in a standard
    // build, and Color's are float); the order is the constructor's, which is also the writer's (:2039-2147).
    private static readonly Dictionary<GodotRealKind, ConstructorSpec> RealSpecs = new()
    {
        [GodotRealKind.Vector2] = new("Vector2", 2, []),
        [GodotRealKind.Vector3] = new("Vector3", 3, []),
        [GodotRealKind.Vector4] = new("Vector4", 4, []),
        [GodotRealKind.Rect2] = new("Rect2", 4, []),
        [GodotRealKind.Plane] = new("Plane", 4, []),
        [GodotRealKind.Quaternion] = new("Quaternion", 4, ["Quat"]),
        [GodotRealKind.Aabb] = new("AABB", 6, ["Rect3"]),
        [GodotRealKind.Transform2D] = new("Transform2D", 6, ["Matrix32"]),
        [GodotRealKind.Basis] = new("Basis", 9, ["Matrix3"]),
        [GodotRealKind.Transform3D] = new("Transform3D", 12, ["Transform"]),
        [GodotRealKind.Projection] = new("Projection", 16, []),
        [GodotRealKind.Color] = new("Color", 4, []),
    };

    // variant_parser.cpp:722-812: the int32_t constructors.
    private static readonly Dictionary<GodotIntKind, ConstructorSpec> IntSpecs = new()
    {
        [GodotIntKind.Vector2I] = new("Vector2i", 2, []),
        [GodotIntKind.Vector3I] = new("Vector3i", 3, []),
        [GodotIntKind.Vector4I] = new("Vector4i", 4, []),
        [GodotIntKind.Rect2I] = new("Rect2i", 4, []),
    };

    // variant_parser.cpp:1547-1618: the flat packed arrays, the arity being the components per element.
    private static readonly Dictionary<GodotPackedKind, ConstructorSpec> PackedSpecs = new()
    {
        [GodotPackedKind.Vector2] = new("PackedVector2Array", 2, ["PoolVector2Array", "Vector2Array"]),
        [GodotPackedKind.Vector3] = new("PackedVector3Array", 3, ["PoolVector3Array", "Vector3Array"]),
        [GodotPackedKind.Vector4] = new("PackedVector4Array", 4, ["PoolVector4Array", "Vector4Array"]),
        [GodotPackedKind.Color] = new("PackedColorArray", 4, ["PoolColorArray", "ColorArray"]),
    };

    // variant_parser.cpp:130-148, tk_name.
    private static readonly Dictionary<GodotTokenType, string> TokenNames = new()
    {
        [GodotTokenType.CurlyBracketOpen] = "'{'",
        [GodotTokenType.CurlyBracketClose] = "'}'",
        [GodotTokenType.BracketOpen] = "'['",
        [GodotTokenType.BracketClose] = "']'",
        [GodotTokenType.ParenthesisOpen] = "'('",
        [GodotTokenType.ParenthesisClose] = "')'",
        [GodotTokenType.Identifier] = "identifier",
        [GodotTokenType.String] = "string",
        [GodotTokenType.StringName] = "string_name",
        [GodotTokenType.Number] = "number",
        [GodotTokenType.Color] = "color",
        [GodotTokenType.Colon] = "':'",
        [GodotTokenType.Comma] = "','",
        [GodotTokenType.Period] = "'.'",
        [GodotTokenType.Equal] = "'='",
        [GodotTokenType.Eof] = "EOF",
    };

    /// <summary>PackedByteArray and its legacy names (variant_parser.cpp:1411).</summary>
    public static ConstructorSpec PackedBytes { get; } = new("PackedByteArray", 1, ["PoolByteArray", "ByteArray"]);

    /// <summary>PackedInt32Array and its legacy names (variant_parser.cpp:1429).</summary>
    public static ConstructorSpec PackedInts { get; } = new("PackedInt32Array", 1, ["PackedIntArray", "PoolIntArray", "IntArray"]);

    /// <summary>PackedInt64Array (variant_parser.cpp:1447).</summary>
    public static ConstructorSpec PackedLongs { get; } = new("PackedInt64Array", 1, []);

    /// <summary>PackedFloat32Array and its legacy names (variant_parser.cpp:1465).</summary>
    public static ConstructorSpec PackedFloats { get; } = new("PackedFloat32Array", 1, ["PackedRealArray", "PoolRealArray", "FloatArray"]);

    /// <summary>PackedFloat64Array (variant_parser.cpp:1483).</summary>
    public static ConstructorSpec PackedDoubles { get; } = new("PackedFloat64Array", 1, []);

    /// <summary>PackedStringArray and its legacy names (variant_parser.cpp:1501).</summary>
    public static ConstructorSpec PackedStrings { get; } = new("PackedStringArray", 1, ["PoolStringArray", "StringArray"]);

    /// <summary>Every flat packed array of vectors or colours, by the type it builds.</summary>
    public static IReadOnlyDictionary<GodotPackedKind, ConstructorSpec> PackedReals => PackedSpecs;

    /// <summary>Every float-component constructor, by the type it builds.</summary>
    public static IReadOnlyDictionary<GodotRealKind, ConstructorSpec> Reals => RealSpecs;

    /// <summary>Every int-component constructor, by the type it builds.</summary>
    public static IReadOnlyDictionary<GodotIntKind, ConstructorSpec> Ints => IntSpecs;

    /// <summary>The constructor of a float-component type.</summary>
    /// <param name="kind">The type.</param>
    /// <returns>Its spec.</returns>
    public static ConstructorSpec Real(GodotRealKind kind) => RealSpecs[kind];

    /// <summary>The constructor of an int-component type.</summary>
    /// <param name="kind">The type.</param>
    /// <returns>Its spec.</returns>
    public static ConstructorSpec Int(GodotIntKind kind) => IntSpecs[kind];

    /// <summary>The flat packed array of a vector or colour type.</summary>
    /// <param name="kind">The type.</param>
    /// <returns>Its spec, whose arity is the components per element.</returns>
    public static ConstructorSpec Packed(GodotPackedKind kind) => PackedSpecs[kind];

    /// <summary>A value's Variant type name, as the metadata file spells a property's type (<c>int</c>, <c>Vector3i</c>).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The name; null for a null value, which has no type; <c>Array</c> and <c>Dictionary</c> for typed ones too.</returns>
    /// <exception cref="ArgumentException">The value is of a record this method does not know.</exception>
    public static string? TypeName(GodotValue value) =>
        value switch
        {
            GodotNull => null,
            GodotBool => "bool",
            GodotInt => "int",
            GodotFloat => "float",
            GodotString => "String",
            GodotStringName => "StringName",
            GodotNodePath => "NodePath",
            GodotReal real => Real(real.Kind).Name,
            GodotInts ints => Int(ints.Kind).Name,
            GodotRid => "RID",
            GodotSignal => "Signal",
            GodotCallable => "Callable",
            GodotArray => "Array",
            GodotDictionary => "Dictionary",
            GodotPackedBytes => PackedBytes.Name,
            GodotPackedInts => PackedInts.Name,
            GodotPackedLongs => PackedLongs.Name,
            GodotPackedFloats => PackedFloats.Name,
            GodotPackedDoubles => PackedDoubles.Name,
            GodotPackedStrings => PackedStrings.Name,
            GodotPackedReals packed => Packed(packed.Kind).Name,
            GodotResourceRef or GodotPathRef or GodotObject => "Object",
            _ => throw new ArgumentException($"A {value.GetType().Name} has no Variant type name.", nameof(value)),
        };

    /// <summary>An array given to a record, which must not be the default (uninitialised) one.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The array.</param>
    /// <param name="name">The parameter's name.</param>
    /// <returns>The array.</returns>
    public static ImmutableArray<T> Required<T>(ImmutableArray<T> items, string name) =>
        items.IsDefault ? throw new ArgumentException("The array is uninitialised; pass an empty array instead.", name) : items;

    /// <summary>Godot's name for a token type in its error messages.</summary>
    /// <param name="type">The token type.</param>
    /// <returns>The name, quoted the way tk_name quotes it.</returns>
    public static string TokenName(GodotTokenType type) => TokenNames[type];

    /// <summary>Throws when a component count does not match a constructor's argument count.</summary>
    /// <param name="spec">The constructor.</param>
    /// <param name="count">The component count.</param>
    public static void CheckArity(ConstructorSpec spec, int count)
    {
        if (count != spec.Arity)
        {
            throw new ArgumentException($"{spec.Name} takes {spec.Arity} components, not {count}.", nameof(count));
        }
    }

    /// <summary>A hash of a kind and its components, for the records whose components are an array.</summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="kind">The kind, as an int.</param>
    /// <param name="components">The components.</param>
    /// <returns>The hash.</returns>
    public static int HashOf<T>(int kind, ImmutableArray<T> components)
    {
        var hash = new HashCode();
        hash.Add(kind);
        foreach (T component in components)
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }
}
