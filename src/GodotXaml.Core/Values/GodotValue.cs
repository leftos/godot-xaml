using System.Collections.Immutable;

namespace GodotXaml.Core.Values;

/// <summary>
/// A value in Godot's text grammar (the right-hand side of <c>name = value</c> in a .tscn or .tres), kept as the
/// structured value Godot reads, so that writing it gives Godot's own text. The set of kinds is closed.
/// </summary>
public abstract record GodotValue
{
    private protected GodotValue() { }
}

/// <summary><c>null</c>; read from <c>null</c> or the legacy <c>nil</c>.</summary>
public sealed record GodotNull : GodotValue
{
    /// <summary>The one null value.</summary>
    public static GodotNull Instance { get; } = new();
}

/// <summary>A bool, <c>true</c> or <c>false</c>.</summary>
/// <param name="Value">The value.</param>
public sealed record GodotBool(bool Value) : GodotValue;

/// <summary>A 64-bit int.</summary>
/// <param name="Value">The value.</param>
public sealed record GodotInt(long Value) : GodotValue;

/// <summary>A top-level float, which Godot holds as a double.</summary>
/// <param name="Value">The value.</param>
public sealed record GodotFloat(double Value) : GodotValue;

/// <summary>A String, unescaped.</summary>
/// <param name="Value">The text.</param>
public sealed record GodotString(string Value) : GodotValue;

/// <summary>A StringName (<c>&amp;"..."</c>), unescaped.</summary>
/// <param name="Value">The text.</param>
public sealed record GodotStringName(string Value) : GodotValue;

/// <summary>
/// A NodePath, held as the text Godot prints for the path it was given: its names joined by <c>/</c> under a leading
/// <c>/</c> when absolute, then its subnames joined by <c>:</c> (core/string/node_path.cpp:170-188, 394-472).
/// </summary>
public sealed record GodotNodePath : GodotValue
{
    /// <summary>Creates a path from the text Godot was given.</summary>
    /// <param name="text">The path text, parsed as Godot parses it.</param>
    public GodotNodePath(string text) => Path = GodotNodePathText.Normalize(text);

    /// <summary>The path text, as Godot prints it.</summary>
    public string Path { get; }
}

/// <summary>A float-component math value (Vector2 to Projection, and Color), its components in constructor order.</summary>
public sealed record GodotReal : GodotValue
{
    /// <summary>Creates a value of the given kind.</summary>
    /// <param name="kind">The value's type.</param>
    /// <param name="components">The components, as many as the kind's constructor takes.</param>
    public GodotReal(GodotRealKind kind, ImmutableArray<float> components)
    {
        GodotValueTables.CheckArity(GodotValueTables.Real(kind), components.Length);
        Kind = kind;
        Components = components;
    }

    /// <summary>The value's type.</summary>
    public GodotRealKind Kind { get; }

    /// <summary>The components, in the order of the type's text constructor.</summary>
    public ImmutableArray<float> Components { get; }

    /// <inheritdoc />
    public bool Equals(GodotReal? other) => other is not null && Kind == other.Kind && Components.SequenceEqual(other.Components);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf((int)Kind, Components);
}

/// <summary>An int-component math value (Vector2i, Vector3i, Vector4i, Rect2i), its components in constructor order.</summary>
public sealed record GodotInts : GodotValue
{
    /// <summary>Creates a value of the given kind.</summary>
    /// <param name="kind">The value's type.</param>
    /// <param name="components">The components, as many as the kind's constructor takes.</param>
    public GodotInts(GodotIntKind kind, ImmutableArray<int> components)
    {
        GodotValueTables.CheckArity(GodotValueTables.Int(kind), components.Length);
        Kind = kind;
        Components = components;
    }

    /// <summary>The value's type.</summary>
    public GodotIntKind Kind { get; }

    /// <summary>The components, in the order of the type's text constructor.</summary>
    public ImmutableArray<int> Components { get; }

    /// <inheritdoc />
    public bool Equals(GodotInts? other) => other is not null && Kind == other.Kind && Components.SequenceEqual(other.Components);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf((int)Kind, Components);
}

/// <summary>A RID: <c>RID()</c> when <see cref="Id"/> is null, else <c>RID(id)</c>.</summary>
public sealed record GodotRid : GodotValue
{
    /// <summary>Creates a RID; an id of 0 is the empty RID, as <c>RID::from_uint64(0)</c> is.</summary>
    /// <param name="id">The id, or null for the empty RID.</param>
    public GodotRid(ulong? id) => Id = id == 0 ? null : id;

    /// <summary>The id, or null for the empty RID.</summary>
    public ulong? Id { get; }
}

/// <summary>A Signal, which Godot writes and reads only as the empty <c>Signal()</c>.</summary>
public sealed record GodotSignal : GodotValue
{
    /// <summary>The one empty signal.</summary>
    public static GodotSignal Instance { get; } = new();
}

/// <summary>A Callable, which Godot writes and reads only as the empty <c>Callable()</c>.</summary>
public sealed record GodotCallable : GodotValue
{
    /// <summary>The one empty callable.</summary>
    public static GodotCallable Instance { get; } = new();
}

/// <summary>An Array, <c>[a, b]</c>, or a typed one, <c>Array[T]([a, b])</c>.</summary>
public sealed record GodotArray : GodotValue
{
    /// <summary>Creates an array.</summary>
    /// <param name="items">The elements, in order.</param>
    /// <param name="elementType">The element type of a typed array, or null for an untyped one.</param>
    public GodotArray(ImmutableArray<GodotValue> items, GodotTypeRef? elementType)
    {
        Items = GodotValueTables.Required(items, nameof(items));
        ElementType = elementType;
    }

    /// <summary>The elements, in order.</summary>
    public ImmutableArray<GodotValue> Items { get; }

    /// <summary>The element type of a typed array, or null for an untyped one.</summary>
    public GodotTypeRef? ElementType { get; }

    /// <inheritdoc />
    public bool Equals(GodotArray? other) => other is not null && ElementType == other.ElementType && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ElementType, GodotValueTables.HashOf(0, Items));
}

/// <summary>A Dictionary entry.</summary>
/// <param name="Key">The key.</param>
/// <param name="Value">The value.</param>
public sealed record GodotEntry(GodotValue Key, GodotValue Value);

/// <summary>
/// A Dictionary, <c>{ key: value }</c>, or a typed one, <c>Dictionary[K, V]({ ... })</c>. Entries keep the order they
/// were given in; the writer sorts them as Godot does. A key given twice is one entry, as in Godot's dictionary
/// (variant_parser.cpp:1743, variant.cpp:3400-3411): it keeps its first place and its last value, and a String and a
/// StringName with the same text are the same key.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Named for Godot's Dictionary type, as every value record is named for its Godot type."
)]
public sealed record GodotDictionary : GodotValue
{
    /// <summary>Creates a dictionary.</summary>
    /// <param name="entries">The entries, in order.</param>
    /// <param name="keyType">The key type of a typed dictionary, or null for any key (<c>Variant</c>).</param>
    /// <param name="valueType">The value type of a typed dictionary, or null for any value (<c>Variant</c>).</param>
    public GodotDictionary(ImmutableArray<GodotEntry> entries, GodotTypeRef? keyType, GodotTypeRef? valueType)
    {
        Entries = GodotKeyOrder.Merge(GodotValueTables.Required(entries, nameof(entries)));
        KeyType = keyType;
        ValueType = valueType;
    }

    /// <summary>The entries, one per distinct key, in the order the keys first came.</summary>
    public ImmutableArray<GodotEntry> Entries { get; }

    /// <summary>The key type, or null for any key; the dictionary is typed when either type is set.</summary>
    public GodotTypeRef? KeyType { get; }

    /// <summary>The value type, or null for any value; the dictionary is typed when either type is set.</summary>
    public GodotTypeRef? ValueType { get; }

    /// <inheritdoc />
    public bool Equals(GodotDictionary? other) =>
        other is not null && KeyType == other.KeyType && ValueType == other.ValueType && GodotKeyOrder.SameEntries(Entries, other.Entries);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(KeyType, ValueType, GodotKeyOrder.HashOfEntries(Entries));
}

/// <summary>
/// The element type of a typed container: a builtin or class name as text, or a script reference. The name is not
/// checked, since the core has no class list.
/// </summary>
public sealed record GodotTypeRef
{
    /// <summary>A type given by name (<c>int</c>, <c>Material</c>).</summary>
    /// <param name="name">The name.</param>
    public GodotTypeRef(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>A type given by a script reference (<c>ExtResource("1")</c>, <c>Resource("res://a.gd")</c>).</summary>
    /// <param name="script">A <see cref="GodotResourceRef"/> or a <see cref="GodotPathRef"/>.</param>
    public GodotTypeRef(GodotValue script)
    {
        Script = script is GodotResourceRef or GodotPathRef
            ? script
            : throw new ArgumentException("A script type is a GodotResourceRef or a GodotPathRef.", nameof(script));
    }

    /// <summary>The type's name, or null for a script type.</summary>
    public string? Name { get; }

    /// <summary>The script reference, or null for a named type.</summary>
    public GodotValue? Script { get; }
}

/// <summary>A PackedByteArray; written as a number list with compat on and as base64 with it off.</summary>
public sealed record GodotPackedBytes : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The bytes.</param>
    public GodotPackedBytes(ImmutableArray<byte> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The bytes.</summary>
    public ImmutableArray<byte> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedBytes? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedInt32Array.</summary>
public sealed record GodotPackedInts : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The elements.</param>
    public GodotPackedInts(ImmutableArray<int> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The elements.</summary>
    public ImmutableArray<int> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedInts? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedInt64Array.</summary>
public sealed record GodotPackedLongs : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The elements.</param>
    public GodotPackedLongs(ImmutableArray<long> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The elements.</summary>
    public ImmutableArray<long> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedLongs? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedFloat32Array.</summary>
public sealed record GodotPackedFloats : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The elements.</param>
    public GodotPackedFloats(ImmutableArray<float> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The elements.</summary>
    public ImmutableArray<float> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedFloats? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedFloat64Array.</summary>
public sealed record GodotPackedDoubles : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The elements.</param>
    public GodotPackedDoubles(ImmutableArray<double> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The elements.</summary>
    public ImmutableArray<double> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedDoubles? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedStringArray, its elements unescaped.</summary>
public sealed record GodotPackedStrings : GodotValue
{
    /// <summary>Creates the array.</summary>
    /// <param name="items">The elements.</param>
    public GodotPackedStrings(ImmutableArray<string> items) => Items = GodotValueTables.Required(items, nameof(items));

    /// <summary>The elements.</summary>
    public ImmutableArray<string> Items { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedStrings? other) => other is not null && Items.SequenceEqual(other.Items, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf(0, Items);
}

/// <summary>A PackedVector2Array, PackedVector3Array, PackedVector4Array or PackedColorArray, its components flat.</summary>
public sealed record GodotPackedReals : GodotValue
{
    /// <summary>Creates an array of the given kind.</summary>
    /// <param name="kind">The array's type.</param>
    /// <param name="components">The elements' components in order, a whole number of elements.</param>
    public GodotPackedReals(GodotPackedKind kind, ImmutableArray<float> components)
    {
        int width = GodotValueTables.Packed(kind).Arity;
        Components = GodotValueTables.Required(components, nameof(components));
        if (components.Length % width != 0)
        {
            throw new ArgumentException(
                $"{kind} elements have {width} components; {components.Length} is not a whole number of them.",
                nameof(components)
            );
        }

        Kind = kind;
    }

    /// <summary>The array's type.</summary>
    public GodotPackedKind Kind { get; }

    /// <summary>The elements' components, flat: x, y for Vector2; r, g, b, a for Color.</summary>
    public ImmutableArray<float> Components { get; }

    /// <inheritdoc />
    public bool Equals(GodotPackedReals? other) => other is not null && Kind == other.Kind && Components.SequenceEqual(other.Components);

    /// <inheritdoc />
    public override int GetHashCode() => GodotValueTables.HashOf((int)Kind, Components);
}

/// <summary>
/// A reference to a resource of the same file by its id: <c>ExtResource("id")</c> or <c>SubResource("id")</c>. A legacy
/// number id (<c>ExtResource(3)</c>) is read as its text (scene/resources/resource_format_text.cpp:105-134).
/// </summary>
/// <param name="Kind">External or sub-resource.</param>
/// <param name="Id">The id.</param>
public sealed record GodotResourceRef(GodotRefKind Kind, string Id) : GodotValue;

/// <summary>A reference to a resource by uid, path or both: <c>Resource("uid://…", "res://…")</c>.</summary>
public sealed record GodotPathRef : GodotValue
{
    /// <summary>Creates a reference; at least one of the two is set.</summary>
    /// <param name="uid">The <c>uid://</c> text, or null.</param>
    /// <param name="path">The path, or null.</param>
    public GodotPathRef(string? uid, string? path)
    {
        if (uid is null && path is null)
        {
            throw new ArgumentException("A Resource reference has a uid, a path or both.", nameof(path));
        }

        Uid = uid;
        Path = path;
    }

    /// <summary>The <c>uid://</c> text, or null.</summary>
    public string? Uid { get; }

    /// <summary>The path, or null.</summary>
    public string? Path { get; }
}

/// <summary>
/// An object that is not a resource with a file, <c>Object(Class,"name":value,...)</c>: its class name and its
/// properties in order. The class is not checked or instantiated.
/// </summary>
public sealed record GodotObject : GodotValue
{
    /// <summary>Creates an object.</summary>
    /// <param name="className">The class name.</param>
    /// <param name="properties">The properties, in order.</param>
    public GodotObject(string className, ImmutableArray<GodotProperty> properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ClassName = className;
        Properties = GodotValueTables.Required(properties, nameof(properties));
    }

    /// <summary>The class name.</summary>
    public string ClassName { get; }

    /// <summary>The properties, in order.</summary>
    public ImmutableArray<GodotProperty> Properties { get; }

    /// <inheritdoc />
    public bool Equals(GodotObject? other) =>
        other is not null && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal) && Properties.SequenceEqual(other.Properties);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ClassName, GodotValueTables.HashOf(0, Properties));
}

/// <summary>A property of a <see cref="GodotObject"/>.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Value">The value.</param>
public sealed record GodotProperty(string Name, GodotValue Value);

/// <summary>The two kinds of <see cref="GodotResourceRef"/>.</summary>
public enum GodotRefKind
{
    /// <summary><c>ExtResource("id")</c>, a resource in another file.</summary>
    External,

    /// <summary><c>SubResource("id")</c>, a resource built into this file.</summary>
    Sub,
}

/// <summary>The types a <see cref="GodotPackedReals"/> holds.</summary>
public enum GodotPackedKind
{
    /// <summary>PackedVector2Array, two components per element.</summary>
    Vector2,

    /// <summary>PackedVector3Array, three components per element.</summary>
    Vector3,

    /// <summary>PackedVector4Array, four components per element.</summary>
    Vector4,

    /// <summary>PackedColorArray, four components per element.</summary>
    Color,
}

/// <summary>The types a <see cref="GodotReal"/> holds.</summary>
public enum GodotRealKind
{
    /// <summary>Vector2(x, y).</summary>
    Vector2,

    /// <summary>Vector3(x, y, z).</summary>
    Vector3,

    /// <summary>Vector4(x, y, z, w).</summary>
    Vector4,

    /// <summary>Rect2(x, y, width, height).</summary>
    Rect2,

    /// <summary>Plane(a, b, c, d).</summary>
    Plane,

    /// <summary>Quaternion(x, y, z, w).</summary>
    Quaternion,

    /// <summary>AABB(x, y, z, sx, sy, sz).</summary>
    Aabb,

    /// <summary>Transform2D, its three columns' x and y.</summary>
    Transform2D,

    /// <summary>Basis, its three rows' x, y and z.</summary>
    Basis,

    /// <summary>Transform3D, its basis rows then its origin.</summary>
    Transform3D,

    /// <summary>Projection, its four columns' x, y, z and w.</summary>
    Projection,

    /// <summary>Color(r, g, b, a).</summary>
    Color,
}

/// <summary>The types a <see cref="GodotInts"/> holds.</summary>
public enum GodotIntKind
{
    /// <summary>Vector2i(x, y).</summary>
    Vector2I,

    /// <summary>Vector3i(x, y, z).</summary>
    Vector3I,

    /// <summary>Vector4i(x, y, z, w).</summary>
    Vector4I,

    /// <summary>Rect2i(x, y, width, height).</summary>
    Rect2I,
}
