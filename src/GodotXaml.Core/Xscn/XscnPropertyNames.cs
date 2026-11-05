using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;
using System.Xml;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Names;

namespace GodotXaml.Core.Xscn;

/// <summary>The Godot property an attribute names, and the type its value is read by.</summary>
/// <param name="GodotName">The property's Godot name (<c>custom_minimum_size</c>).</param>
/// <param name="Type">The value's type; unknown for a script property or a name the classes disagree on.</param>
public sealed record XscnResolvedProperty(string GodotName, XscnValueType Type);

/// <summary>
/// Maps an attribute's name to a Godot property and back: by C# name on a known class, by the C# name every class agrees
/// on for an element of unknown class, dotted for a slash-named property, and verbatim for a script property.
/// </summary>
public sealed class XscnPropertyNames
{
    private readonly GodotMetadata metadata;
    private readonly FrozenDictionary<string, XscnResolvedProperty?> byCSharpName;
    private readonly FrozenDictionary<string, string?> csNameByGodotName;
    private readonly FrozenDictionary<string, XscnValueType> familyTypes;
    private readonly ConcurrentDictionary<object, XscnValueType> types = new(ReferenceEqualityComparer.Instance);

    /// <summary>Indexes every class's properties and theme override families.</summary>
    /// <param name="metadata">The metadata.</param>
    public XscnPropertyNames(GodotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        this.metadata = metadata;
        List<GodotPropertyInfo> properties = [.. metadata.Classes.SelectMany(c => c.Properties).Where(p => !p.Name.Contains('/'))];
        byCSharpName = properties.GroupBy(p => p.CSharpName, StringComparer.Ordinal).ToFrozenDictionary(g => g.Key, Agreed, StringComparer.Ordinal);
        csNameByGodotName = properties
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => Single(g.Select(p => p.CSharpName)), StringComparer.Ordinal);
        familyTypes = FamilyTypes(metadata);
    }

    /// <summary>The property an attribute names.</summary>
    /// <param name="cls">The element's class, or null when it is not known (an instance, an override, an unknown class).</param>
    /// <param name="attributeName">The attribute's name, without a prefix.</param>
    /// <returns>The property; a name no class has is a script property, its name verbatim and its type unknown.</returns>
    public XscnResolvedProperty Resolve(GodotClassInfo? cls, string attributeName)
    {
        ArgumentNullException.ThrowIfNull(attributeName);
        if (SlashedName(attributeName) is { } godotName)
        {
            return ResolveSlashed(cls, godotName);
        }

        return cls is null ? ResolveUnknownClass(attributeName) : ResolveOnClass(cls, attributeName);
    }

    /// <summary>The attribute name of a property.</summary>
    /// <param name="cls">The element's class, or null when it is not known.</param>
    /// <param name="godotName">The property's Godot name.</param>
    /// <returns>
    /// The name that <see cref="Resolve"/> maps back to <paramref name="godotName"/>, or null when there is none and the
    /// property needs <c>x:Prop</c> (a key that is not an XML name, an empty key such as the default animation library's
    /// <c>libraries/</c>, a script property named like an engine property).
    /// </returns>
    public string? AttributeName(GodotClassInfo? cls, string godotName)
    {
        ArgumentNullException.ThrowIfNull(godotName);
        if (godotName.EndsWith('/'))
        {
            return null;
        }

        string candidate = Candidate(cls, godotName);
        return IsXmlName(candidate) && GodotNameOf(cls, candidate) == godotName ? candidate : null;
    }

    private static XscnResolvedProperty Script(string name) => new(name, XscnValueType.Unknown);

    // "Family.key" names family_snake/key; null for an attribute name without a dot after its first character.
    private static string? SlashedName(string attributeName)
    {
        int dot = attributeName.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? PascalToSnake(attributeName[..dot]) + "/" + attributeName[(dot + 1)..] : null;
    }

    // The Godot name Resolve gives, found without typing the value.
    private string GodotNameOf(GodotClassInfo? cls, string attributeName)
    {
        string? godotName =
            SlashedName(attributeName)
            ?? (cls is null ? byCSharpName.GetValueOrDefault(attributeName)?.GodotName : metadata.FindPropertyByCSharpName(cls, attributeName)?.Name);
        return godotName ?? attributeName;
    }

    // Typing a property builds its enum names, so each property's and theme override item's type is made once; a property's
    // type is its own, whichever class's element it is on.
    /// <summary>The type a property's attribute is read and written by, built once per property.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The type <see cref="XscnValueText.TypeOf(GodotMetadata, GodotPropertyInfo)"/> gives for this index's metadata.</returns>
    public XscnValueType TypeOf(GodotPropertyInfo property) => types.GetOrAdd(property, _ => XscnValueText.TypeOf(metadata, property));

    private XscnValueType TypeOf(GodotThemeOverrideInfo item) => types.GetOrAdd(item, _ => XscnValueText.TypeOf(item));

    // A slash-named property is "Family.key": the part before the first '/' in PascalCase, the rest verbatim.
    private string Candidate(GodotClassInfo? cls, string godotName)
    {
        int slash = godotName.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            return GodotNames.SnakeToPascalCase(godotName[..slash], inputIsUpper: false) + "." + godotName[(slash + 1)..];
        }

        string? csName = cls is null ? csNameByGodotName.GetValueOrDefault(godotName) : metadata.FindProperty(cls, godotName)?.CSharpName;
        return csName ?? godotName;
    }

    private XscnResolvedProperty ResolveOnClass(GodotClassInfo cls, string attributeName)
    {
        GodotPropertyInfo? property = metadata.FindPropertyByCSharpName(cls, attributeName);
        return property is null ? Script(attributeName) : new XscnResolvedProperty(property.Name, TypeOf(property));
    }

    private XscnResolvedProperty ResolveUnknownClass(string attributeName) => byCSharpName.GetValueOrDefault(attributeName) ?? Script(attributeName);

    // A slash-named property's type: the class's own property or theme override item, else its theme override family's
    // fixed type; metadata/… and shader_parameter/… have none.
    private XscnResolvedProperty ResolveSlashed(GodotClassInfo? cls, string godotName)
    {
        GodotPropertyInfo? property = cls is null ? null : metadata.FindProperty(cls, godotName);
        GodotThemeOverrideInfo? item = cls is null ? null : metadata.FindThemeOverride(cls, godotName);
        XscnValueType type =
            property is not null ? TypeOf(property)
            : item is not null ? TypeOf(item)
            : familyTypes.GetValueOrDefault(godotName[..godotName.IndexOf('/', StringComparison.Ordinal)], XscnValueType.Unknown);
        return new XscnResolvedProperty(godotName, type);
    }

    // The classes that have a C# name agree when they share one Godot name; the value is typed only when they also share
    // its type, enum and, for an int with a hint but no enum, the hint.
    private XscnResolvedProperty? Agreed(IGrouping<string, GodotPropertyInfo> sameName)
    {
        string? godotName = Single(sameName.Select(p => p.Name));
        if (godotName is null)
        {
            return null;
        }

        bool typed = sameName.Select(TypingKey).Distinct().Count() == 1;
        return new XscnResolvedProperty(godotName, typed ? TypeOf(sameName.First()) : XscnValueType.Unknown);
    }

    private (string? Type, string? EnumType, bool Bitfield, string? Hint) TypingKey(GodotPropertyInfo property)
    {
        XscnValueType type = TypeOf(property);
        string? hint = property.EnumType is null && type.Names is not null ? property.Hint + ":" + property.HintString : null;
        return (type.VariantType, property.EnumType, property.Bitfield, hint);
    }

    private static string? Single(IEnumerable<string> names)
    {
        List<string> distinct = [.. names.Distinct(StringComparer.Ordinal).Take(2)];
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // Each theme override family has one type (scene/gui/control.cpp:400-417); a family whose items disagree has none.
    private static FrozenDictionary<string, XscnValueType> FamilyTypes(GodotMetadata metadata) =>
        metadata
            .Classes.SelectMany(c => c.ThemeOverrides)
            .GroupBy(t => t.Name[..Math.Max(t.Name.IndexOf('/', StringComparison.Ordinal), 0)], StringComparer.Ordinal)
            .Where(g => g.Key.Length > 0 && g.Select(t => t.Type).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToFrozenDictionary(g => g.Key, g => XscnValueText.TypeOf(g.First()), StringComparer.Ordinal);

    // The inverse of GodotNames.SnakeToPascalCase for a family name: '_' before each capital but the first, lower case.
    private static string PascalToSnake(string pascal)
    {
        var snake = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(pascal[i]));
        }

        return snake.ToString();
    }

    private static bool IsXmlName(string name) =>
        name.Length > 0 && XmlConvert.IsStartNCNameChar(name[0]) && name.Skip(1).All(XmlConvert.IsNCNameChar);
}
