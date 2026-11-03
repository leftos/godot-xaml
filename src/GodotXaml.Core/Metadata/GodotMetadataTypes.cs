using System.Collections.Frozen;
using System.Collections.Immutable;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Metadata;

/// <summary>An engine class as the metadata file lists it: its own properties, enums and signals, and its parent.</summary>
public sealed class GodotClassInfo
{
    private readonly FrozenDictionary<string, GodotPropertyInfo> propertiesByName;
    private readonly FrozenDictionary<string, GodotPropertyInfo> propertiesByCSharpName;
    private readonly FrozenDictionary<string, GodotEnumInfo> enumsByName;
    private readonly FrozenDictionary<string, GodotThemeOverrideInfo> themeOverridesByName;
    private readonly FrozenDictionary<string, GodotPropertyDefault> defaultOverrides;

    internal GodotClassInfo(GodotClassParts parts)
    {
        Name = parts.Name;
        CSharpName = parts.CSharpName;
        Parent = parts.Parent;
        Instantiable = parts.Instantiable;
        HasDefaults = parts.HasDefaults;
        Properties = parts.Properties;
        ListOrder = parts.ListOrder;
        Enums = parts.Enums;
        ThemeOverrides = parts.ThemeOverrides;
        Signals = parts.Signals;
        propertiesByName = Index(parts.Properties, p => p.Name, "property");
        propertiesByCSharpName = Index(parts.Properties, p => p.CSharpName, "property C# name");
        defaultOverrides = UniqueIndex.Build(
            parts.DefaultOverrides,
            o => o.Key,
            o => o.Value,
            duplicate => $"Class '{Name}' lists the default override '{duplicate}' twice."
        );
        enumsByName = Index(parts.Enums, e => e.QualifiedName[(e.QualifiedName.LastIndexOf('.') + 1)..], "enum");
        themeOverridesByName = Index(parts.ThemeOverrides, t => t.Name, "theme override");
    }

    /// <summary>The class's Godot name (<c>CPUParticles2D</c>).</summary>
    public string Name { get; }

    /// <summary>The class's C# name (<c>CpuParticles2D</c>).</summary>
    public string CSharpName { get; }

    /// <summary>The parent class's Godot name, or null for <c>Object</c>.</summary>
    public string? Parent { get; }

    /// <summary>Whether ClassDB can instantiate the class.</summary>
    public bool Instantiable { get; }

    /// <summary>
    /// Whether Godot reads any default for the class's properties; false for a class ClassDB marks virtual, whose every property
    /// has none.
    /// </summary>
    public bool HasDefaults { get; }

    /// <summary>The properties the class declares itself, in ClassDB order.</summary>
    public ImmutableArray<GodotPropertyInfo> Properties { get; }

    /// <summary>
    /// The names the class declares itself in ClassDB order, slash-named ones included, for property order only; the names of
    /// <see cref="Properties"/> for a class with no slash-named name.
    /// </summary>
    public ImmutableArray<string> ListOrder { get; }

    /// <summary>The enums the class declares itself.</summary>
    public ImmutableArray<GodotEnumInfo> Enums { get; }

    /// <summary>Every theme override item of the class, inherited ones included; empty for a class with none.</summary>
    public ImmutableArray<GodotThemeOverrideInfo> ThemeOverrides { get; }

    /// <summary>The signals the class declares itself; empty when the metadata file lists none.</summary>
    public ImmutableArray<GodotSignalInfo> Signals { get; }

    internal GodotPropertyInfo? OwnProperty(string godotName) => propertiesByName.GetValueOrDefault(godotName);

    internal GodotPropertyInfo? OwnPropertyByCSharpName(string csName) => propertiesByCSharpName.GetValueOrDefault(csName);

    internal GodotEnumInfo? OwnEnum(string godotName) => enumsByName.GetValueOrDefault(godotName);

    internal GodotThemeOverrideInfo? ThemeOverride(string godotName) => themeOverridesByName.GetValueOrDefault(godotName);

    internal GodotPropertyDefault? DefaultOverride(string godotName) => defaultOverrides.GetValueOrDefault(godotName);

    private FrozenDictionary<string, T> Index<T>(ImmutableArray<T> items, Func<T, string> key, string what) =>
        UniqueIndex.Build(items, key, duplicate => $"Class '{Name}' lists the {what} '{duplicate}' twice.");
}

/// <summary>The parts a <see cref="GodotClassInfo"/> is built from.</summary>
internal sealed record GodotClassParts(
    string Name,
    string CSharpName,
    string? Parent,
    bool Instantiable,
    bool HasDefaults,
    ImmutableArray<GodotPropertyInfo> Properties,
    ImmutableArray<string> ListOrder,
    ImmutableArray<KeyValuePair<string, GodotPropertyDefault>> DefaultOverrides,
    ImmutableArray<GodotEnumInfo> Enums,
    ImmutableArray<GodotThemeOverrideInfo> ThemeOverrides,
    ImmutableArray<GodotSignalInfo> Signals
);

/// <summary>A property a class declares, as the metadata file lists it.</summary>
/// <param name="Name">The Godot name (<c>custom_minimum_size</c>).</param>
/// <param name="CSharpName">The C# name (<c>CustomMinimumSize</c>), with <c>_</c> appended when it equals its class's.</param>
/// <param name="Type">The Variant type name (<c>Vector2</c>).</param>
/// <param name="EnumType">The enum or bitfield type (<c>Control.MouseFilter</c>), or null.</param>
/// <param name="Bitfield">Whether <paramref name="EnumType"/> is a bitfield.</param>
/// <param name="Hint">The property hint.</param>
/// <param name="HintString">The hint string.</param>
/// <param name="Usage">The property usage flags.</param>
/// <param name="ClassName">The class name of an Object property, or empty.</param>
/// <param name="DefaultText">
/// The declaring class's default for the property as Godot value text, or null when no default is known. The default for
/// an element of a given class is <see cref="GodotMetadata.FindDefault"/>.
/// </param>
/// <param name="Default">
/// The declaring class's default read from <paramref name="DefaultText"/>, or null when no default is known. The default
/// for an element of a given class is <see cref="GodotMetadata.FindDefault"/>.
/// </param>
public sealed record GodotPropertyInfo(
    string Name,
    string CSharpName,
    string Type,
    string? EnumType,
    bool Bitfield,
    int Hint,
    string HintString,
    int Usage,
    string ClassName,
    string? DefaultText,
    GodotValue? Default
)
{
    // core/object/object.h, PropertyUsageFlags: PROPERTY_USAGE_STORAGE = 1 << 1.
    private const int UsageStorage = 2;

    /// <summary>A property whose getter returns a type other than the one it is registered with.</summary>
    /// <param name="Name">The Godot name.</param>
    /// <param name="CSharpName">The C# name.</param>
    /// <param name="Type">The registered Variant type name.</param>
    /// <param name="EnumType">The enum or bitfield type, or null.</param>
    /// <param name="Bitfield">Whether <paramref name="EnumType"/> is a bitfield.</param>
    /// <param name="Hint">The property hint.</param>
    /// <param name="HintString">The hint string.</param>
    /// <param name="Usage">The property usage flags.</param>
    /// <param name="ClassName">The class name of an Object property, or empty.</param>
    /// <param name="DefaultText">The declaring class's default as Godot value text, or null.</param>
    /// <param name="Default">The declaring class's default, or null.</param>
    /// <param name="getterType">The <see cref="GetterType"/>, or null.</param>
    public GodotPropertyInfo(
        string Name,
        string CSharpName,
        string Type,
        string? EnumType,
        bool Bitfield,
        int Hint,
        string HintString,
        int Usage,
        string ClassName,
        string? DefaultText,
        GodotValue? Default,
        string? getterType
    )
        : this(Name, CSharpName, Type, EnumType, Bitfield, Hint, HintString, Usage, ClassName, DefaultText, Default)
    {
        GetterType = getterType;
    }

    /// <summary>
    /// The Variant type name the property's getter returns where it differs from <see cref="Type"/> (Godot registers
    /// <c>AudioEffectPitchShift.oversampling</c> as float and its getter returns int), else null. Godot saves the getter's value.
    /// </summary>
    public string? GetterType { get; }

    /// <summary>Whether the property is saved to a scene file (its usage has <c>PROPERTY_USAGE_STORAGE</c>).</summary>
    public bool IsStored => (Usage & UsageStorage) != 0;

    /// <summary>
    /// The Variant type of the property's value as Godot saves it: <see cref="GetterType"/> when the metadata records one,
    /// else <see cref="Type"/>; <c>Nil</c> for a property declared Variant, which holds any kind.
    /// </summary>
    public string ValueType => Type == "Nil" ? Type : GetterType ?? Type;
}

/// <summary>A property's default for a class: its Godot value text (<c>Vector2(0, 0)</c>) and the value read from it.</summary>
/// <param name="Text">The default as Godot value text.</param>
/// <param name="Value">The default read from <paramref name="Text"/>.</param>
public sealed record GodotPropertyDefault(string Text, GodotValue Value);

/// <summary>A class or global enum with its members' Godot and C# names.</summary>
/// <param name="QualifiedName">The class enum's <c>Class.Enum</c> name, or the global enum's name.</param>
/// <param name="Bitfield">Whether the enum is a bitfield.</param>
/// <param name="Members">The members, in declaration order.</param>
public sealed record GodotEnumInfo(string QualifiedName, bool Bitfield, ImmutableArray<GodotEnumMember> Members);

/// <summary>One member of an enum.</summary>
/// <param name="GodotName">The Godot name (<c>MOUSE_FILTER_IGNORE</c>).</param>
/// <param name="CSharpName">The C# name (<c>Ignore</c>).</param>
/// <param name="Value">The member's value.</param>
public sealed record GodotEnumMember(string GodotName, string CSharpName, long Value);

/// <summary>A theme override item of a Control or Window class.</summary>
/// <param name="Name">The property name (<c>theme_override_colors/font_color</c>).</param>
/// <param name="Type">The Variant type name (<c>Color</c>, or <c>Object</c> for a resource).</param>
/// <param name="Hint">The property hint.</param>
/// <param name="HintString">The hint string (<c>StyleBox</c> for a style).</param>
public sealed record GodotThemeOverrideInfo(string Name, string Type, int Hint, string HintString);

/// <summary>A signal a class declares.</summary>
/// <param name="Name">The Godot name (<c>button_down</c>).</param>
/// <param name="CSharpName">
/// The C# name (<c>ButtonDown</c>), with <c>_</c> appended when it equals its class's name and then <c>Signal</c> when it equals one
/// of its class's own property names; null for a signal whose name begins with <c>_</c>, which the C# API leaves out.
/// </param>
/// <param name="Arguments">The signal's arguments, in order.</param>
public sealed record GodotSignalInfo(string Name, string? CSharpName, ImmutableArray<GodotSignalArgument> Arguments);

/// <summary>A lookup by a key that must be unique.</summary>
internal static class UniqueIndex
{
    /// <summary>The items keyed by <paramref name="key"/>, failing on a key that two items share.</summary>
    /// <param name="items">The items.</param>
    /// <param name="key">Each item's key.</param>
    /// <param name="duplicate">The error message for a key found twice.</param>
    /// <returns>The lookup, ordinal on the key.</returns>
    public static FrozenDictionary<string, T> Build<T>(IEnumerable<T> items, Func<T, string> key, Func<string, string> duplicate) =>
        Build(items, key, item => item, duplicate);

    /// <summary>The items' values by their keys, failing on a key that two items share.</summary>
    public static FrozenDictionary<string, TValue> Build<TItem, TValue>(
        IEnumerable<TItem> items,
        Func<TItem, string> key,
        Func<TItem, TValue> value,
        Func<string, string> duplicate
    )
    {
        var index = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (TItem item in items)
        {
            if (!index.TryAdd(key(item), value(item)))
            {
                throw new InvalidDataException(duplicate(key(item)));
            }
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

/// <summary>One argument of a signal.</summary>
/// <param name="Name">The argument's name.</param>
/// <param name="Type">The Variant type name.</param>
/// <param name="ClassName">The class name of an Object argument, or empty.</param>
public sealed record GodotSignalArgument(string Name, string Type, string ClassName);
