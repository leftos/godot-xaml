using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;
using Microsoft.Language.Xml;
using TextSpan = GodotXaml.Core.Diagnostics.TextSpan;

namespace GodotXaml.Core.Xscn;

/// <summary>The attributes: names, properties, signals and the <c>x:</c> attributes.</summary>
internal sealed partial class XscnTreeReader
{
    // core/object/object.h, ConnectFlags: CONNECT_PERSIST = 2, which every connection a scene stores has.
    internal const int Persist = 2;

    private static readonly XscnValueType IntType = new("int", null);
    private static readonly XscnValueType BoolType = new("bool", null);

    // Script's type, which the writer shares.
    internal static readonly XscnValueType ObjectType = new("Object", null);

    // The x: attributes of a node or an override, by local name.
    private static readonly FrozenDictionary<string, AttributeReader> NodeFormatAttributes = new Dictionary<string, AttributeReader>(
        StringComparer.Ordinal
    )
    {
        ["Index"] = (reader, parts, attribute) => parts.Index = reader.ReadInt(attribute),
        ["Owner"] = (_, parts, attribute) => parts.Owner = attribute.Value,
        ["Editable"] = (reader, parts, attribute) => parts.Editable = reader.ReadBool(attribute),
        ["Groups"] = (reader, parts, attribute) => parts.Groups = reader.ReadNameList(attribute),
        ["InstancePlaceholder"] = (_, parts, attribute) => parts.InstancePlaceholder = attribute.Value,
        ["NodePaths"] = (reader, parts, attribute) => parts.NodePaths = reader.ReadNameList(attribute),
        ["ParentIdPath"] = (reader, parts, attribute) => parts.ParentIdPath = reader.ReadIdPath(attribute),
        ["OwnerUidPath"] = (reader, parts, attribute) => parts.OwnerUidPath = reader.ReadIdPath(attribute),
        ["UniqueId"] = (reader, parts, attribute) => parts.UniqueId = reader.ReadInt(attribute),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // The unprefixed attributes that are not properties, by the kind of element that carries them.
    private static readonly FrozenDictionary<(ElementKind, string), AttributeReader> SpecialAttributes = new Dictionary<
        (ElementKind, string),
        AttributeReader
    >
    {
        [(ElementKind.Node, "Name")] = (_, parts, attribute) => SetName(parts, attribute.Value),
        [(ElementKind.Instance, "Name")] = (_, parts, attribute) => SetName(parts, attribute.Value),
        [(ElementKind.Instance, "Scene")] = (_, parts, attribute) => parts.Scene = attribute.Value,
        [(ElementKind.Override, "Path")] = (_, parts, attribute) => parts.Path = attribute.Value,
    }.ToFrozenDictionary();

    private delegate void AttributeReader(XscnTreeReader reader, ElementParts parts, AttributeText attribute);

    private static bool IsNamespaceDeclaration(AttributeText attribute) => IsNamespaceDeclaration(attribute.Prefix, attribute.LocalName);

    private static bool DeclaresNamespace(XmlAttributeSyntax attribute) =>
        IsNamespaceDeclaration(attribute.NameNode?.Prefix ?? "", attribute.NameNode?.LocalName ?? "");

    private static bool IsNamespaceDeclaration(string prefix, string localName) => prefix == "xmlns" || (prefix.Length == 0 && localName == "xmlns");

    private XscnEnumNames ConnectFlags => XscnSignalNames.ConnectFlags(metadata);

    private static void SetName(ElementParts parts, string name)
    {
        parts.Unique = name.StartsWith('%');
        parts.Name = parts.Unique ? name[1..] : name;
    }

    private AttributeText Attribute(XmlAttributeSyntax attribute)
    {
        XmlStringSyntax? value = attribute.ValueNode;
        int start = value is null ? attribute.SpanStart + attribute.Width : value.StartQuoteToken.SpanStart + value.StartQuoteToken.Width;
        string raw = attribute.Value ?? "";
        string decoded = XmlText.DecodeAttribute(raw);
        string prefix = attribute.NameNode?.Prefix ?? "";
        return new AttributeText(prefix, attribute.NameNode?.LocalName ?? "", decoded, SpanOf(attribute), new TextSpan(start, raw.Length));
    }

    // The root's namespace declarations were read before everything else, so they are skipped here, unread.
    private void ReadAttributes(ElementParts parts)
    {
        foreach (AttributeText attribute in FirstOfEachName(parts.Syntax).Where(a => !(parts.IsRoot && DeclaresNamespace(a))).Select(Attribute))
        {
            ReadAttribute(parts, attribute);
        }
    }

    private void ReadAttribute(ElementParts parts, AttributeText attribute)
    {
        if (attribute.Prefix.Length == 0 && !IsNamespaceDeclaration(attribute))
        {
            ReadPlainAttribute(parts, attribute);
        }
        else if (attribute.Prefix == formatPrefix)
        {
            ReadFormatAttribute(parts, attribute);
        }
        else
        {
            UnknownAttribute(parts.Syntax, attribute);
        }
    }

    private void ReadPlainAttribute(ElementParts parts, AttributeText attribute)
    {
        if (SpecialAttributes.TryGetValue((parts.Kind, attribute.LocalName), out AttributeReader? read))
        {
            read(this, parts, attribute);
        }
        else if (attribute.LocalName == "Script")
        {
            parts.Properties.Add(new XscnProperty("script", ReadValue(attribute, ObjectType)));
        }
        else if (parts.IsNode && parts.Class is { } cls && SignalAttribute(cls, attribute.LocalName) is { } signal)
        {
            parts.Connections.Add(new XscnConnection(signal.Name, ".", attribute.Value, Persist, [], 0, [], []));
        }
        else
        {
            XscnResolvedProperty property = names.Resolve(parts.Class, attribute.LocalName);
            parts.Properties.Add(new XscnProperty(property.GodotName, ReadValue(attribute, property.Type)));
        }
    }

    private void ReadFormatAttribute(ElementParts parts, AttributeText attribute)
    {
        if (parts.IsNode && NodeFormatAttributes.TryGetValue(attribute.LocalName, out AttributeReader? read))
        {
            read(this, parts, attribute);
        }
        else if (parts.Kind == ElementKind.TableEntry && attribute.LocalName == "Key")
        {
            parts.Key = attribute.Value;
            parts.KeySpan = attribute.ValueSpan;
        }
        else
        {
            UnknownAttribute(parts.Syntax, attribute);
        }
    }

    private void UnknownAttribute(IXmlElementSyntax element, AttributeText attribute)
    {
        string name = attribute.Prefix.Length == 0 ? attribute.LocalName : attribute.Prefix + ":" + attribute.LocalName;
        if (IsNamespaceDeclaration(attribute))
        {
            Report(XscnCodes.MisplacedNamespace, "Namespace declarations belong on the root element.", attribute.Span);
        }
        else if (sceneFolders.ContainsKey(attribute.Prefix))
        {
            string message = $"The prefix '{attribute.Prefix}' binds a scene folder, which names elements, not attributes; {name} is not read.";
            Report(XscnCodes.ScenePrefixedAttribute, message, attribute.Span);
        }
        else if (attribute.Prefix.Length > 0 && attribute.Prefix != formatPrefix)
        {
            Report(XscnCodes.UndeclaredPrefix, $"The prefix '{attribute.Prefix}' is not declared on the root.", attribute.Span);
        }
        else
        {
            Report(XscnCodes.UnknownFormatAttribute, $"<{FullName(element)}> takes no attribute {name}.", attribute.Span);
        }
    }

    private GodotSignalInfo? SignalAttribute(GodotClassInfo cls, string name) => XscnSignalNames.SignalAttribute(metadata, cls, name);

    // A value that is not text of its type stays in the model as XscnInvalid and is reported over the value's text.
    private XscnValue ReadValue(AttributeText attribute, XscnValueType type)
    {
        XscnValue value = XscnValueText.Read(attribute.Value, type);
        if (value is XscnInvalid invalid)
        {
            Report(type.Names is null ? XscnCodes.InvalidValue : XscnCodes.UnknownEnumMember, invalid.Message, attribute.ValueSpan);
        }

        return value;
    }

    private int? ReadInt(AttributeText attribute)
    {
        XscnValue value = ReadValue(attribute, IntType);
        if (value is XscnScalar { Value: GodotInt { Value: >= int.MinValue and <= int.MaxValue } number })
        {
            return (int)number.Value;
        }

        if (value is XscnScalar)
        {
            Report(XscnCodes.InvalidValue, "Expected an integer that fits in 32 bits.", attribute.ValueSpan);
        }

        return null;
    }

    private bool ReadBool(AttributeText attribute) => ReadValue(attribute, BoolType) is XscnScalar { Value: GodotBool { Value: true } };

    // Names joined with commas, or Godot array text of strings for a name holding a comma or edge whitespace.
    private ImmutableArray<string> ReadNameList(AttributeText attribute)
    {
        if (!attribute.Value.TrimStart().StartsWith('['))
        {
            return [.. attribute.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        }

        if (XscnValueText.Read(attribute.Value, XscnValueType.Unknown) is XscnScalar { Value: GodotArray array } && array.Items.All(IsText))
        {
            return [.. array.Items.Select(TextOf)];
        }

        Report(XscnCodes.InvalidValue, "Expected names joined with commas, or a Godot array of strings.", attribute.ValueSpan);
        return [];
    }

    private static bool IsText(GodotValue value) => value is GodotString or GodotStringName;

    private static string TextOf(GodotValue value) => value is GodotString text ? text.Value : ((GodotStringName)value).Value;

    private ImmutableArray<int> ReadIdPath(AttributeText attribute)
    {
        string[] parts = attribute.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        List<int> ids = [];
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id))
            {
                Report(XscnCodes.InvalidValue, "Expected integers separated by commas.", attribute.ValueSpan);
                return [];
            }

            ids.Add(id);
        }

        return [.. ids];
    }

    /// <summary>An attribute's name, its decoded value, and where both lie.</summary>
    /// <param name="Prefix">The prefix, or empty.</param>
    /// <param name="LocalName">The name after the prefix.</param>
    /// <param name="Value">The value, its references decoded.</param>
    /// <param name="Span">The whole attribute.</param>
    /// <param name="ValueSpan">The value's text between the quotes.</param>
    private sealed record AttributeText(string Prefix, string LocalName, string Value, TextSpan Span, TextSpan ValueSpan);
}
