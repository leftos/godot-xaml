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

/// <summary>A NodePath, held as the raw path text Godot was given.</summary>
/// <param name="Value">The path text.</param>
public sealed record GodotNodePath(string Value) : GodotValue;

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
