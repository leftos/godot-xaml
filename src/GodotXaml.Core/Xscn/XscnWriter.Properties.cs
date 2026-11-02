using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Xscn;

/// <summary>The properties and connections: attributes, property elements, <c>x:Prop</c> and <c>x:Connect</c>.</summary>
public static partial class XscnWriter
{
    private const string ScriptName = "script";
    private const string ScriptAttribute = "Script";
    private const string LayoutModeName = "layout_mode";

    private static string GodotText(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);

    // Godot value text with each character XML cannot hold written as a \uXXXX escape, which Godot's string grammar reads
    // (core/variant/variant_parser.cpp:315-316, the 'u' case of get_token's string escapes). A lone surrogate has no
    // escape: Godot rejects an unpaired one (variant_parser.cpp:357-395), and so does GodotValueLexer.
    private static string EscapeForbidden(string godotText, string what, string element)
    {
        var text = new StringBuilder(godotText.Length + 8);
        for (int i = 0; i < godotText.Length; i++)
        {
            char c = godotText[i];
            if (char.IsHighSurrogate(c) && i + 1 < godotText.Length && char.IsLowSurrogate(godotText[i + 1]))
            {
                text.Append(c).Append(godotText[++i]);
            }
            else if (char.IsSurrogate(c))
            {
                throw new ArgumentException($"{what} of {element} holds the lone surrogate U+{(int)c:X4}, which no file can hold.");
            }
            else
            {
                AppendGodotChar(text, c);
            }
        }

        return text.ToString();
    }

    private static void AppendGodotChar(StringBuilder text, char c)
    {
        if (IsForbidden(c))
        {
            text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append(c);
        }
    }

    /// <summary>What decides how an element's properties are written.</summary>
    /// <param name="Tag">The element's tag as written, the owner of its property elements.</param>
    /// <param name="Class">The element's class, or null when it is not known (an instance, an override, an unknown class).</param>
    /// <param name="Reserved">The attribute names the element uses for something other than a property.</param>
    /// <param name="IsNode">Whether the element is a node, whose attributes may name signals.</param>
    /// <param name="ParentIsContainer">Whether the element is a node whose parent is a Container.</param>
    private sealed record PropertyOwner(string Tag, GodotClassInfo? Class, IReadOnlySet<string> Reserved, bool IsNode, bool ParentIsContainer);

    /// <summary>A property as written: an attribute, or the items of a property element or an <c>x:Prop</c>.</summary>
    /// <param name="GodotName">The property's Godot name, which orders the elements.</param>
    /// <param name="Attribute">The attribute, or null for an element.</param>
    /// <param name="Element">The element and the comments around it; empty for an attribute.</param>
    private sealed record PropertyOut(string GodotName, XmlAttr? Attribute, IReadOnlyList<XmlItem> Element);

    private sealed partial class Session
    {
        // The properties as written, in model order. A Godot name with more than one property is always elements: attributes
        // are read before the elements, so one of them would win where the model's last one wins. None of that name's
        // properties is left out as a default either, since an earlier one would then win instead.
        private List<PropertyOut> Properties(ImmutableArray<XscnProperty> properties, PropertyOwner owner)
        {
            HashSet<string> repeated = [.. properties.GroupBy(p => p.GodotName, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)];
            List<PropertyOut> result = [];
            foreach (XscnProperty property in properties)
            {
                if (repeated.Contains(property.GodotName))
                {
                    result.Add(ElementForm(owner, property));
                }
                else if (!Omitted(owner, property))
                {
                    result.Add(Form(owner, property));
                }
            }

            return result;
        }

        // A property of a repeated name: an element whatever its value, so that model order decides which one wins.
        private PropertyOut ElementForm(PropertyOwner owner, XscnProperty property) =>
            property.Value is XscnInline inline
                ? InlineForm(owner, property, inline.Resource)
                : PropForm(owner.Tag, property, UntypedText(property.Value));

        // A scalar equal to the class's own default, and a scalar layout_mode under a Container, which the parent decides;
        // never on an element whose class is not known, for the script, or where comments would be lost.
        private bool Omitted(PropertyOwner owner, XscnProperty property)
        {
            if (owner.Class is not { } cls || !MayOmit(property) || property.Value is not XscnScalar scalar)
            {
                return false;
            }

            if (owner.ParentIsContainer && property.GodotName == LayoutModeName && metadata.FindProperty(cls, LayoutModeName) is not null)
            {
                return true;
            }

            return metadata.FindDefault(cls, property.GodotName) is { } fallback && GodotText(fallback.Value) == GodotText(scalar.Value);
        }

        private static bool MayOmit(XscnProperty property) =>
            property.GodotName != ScriptName && property.LeadingComments.IsEmpty && property.TrailingComments.IsEmpty;

        // An attribute when its name reads back as this property; x:Prop when it would not, or to keep the comments around it,
        // which a start tag has no room for.
        private PropertyOut Form(PropertyOwner owner, XscnProperty property)
        {
            if (property.Value is XscnInline inline)
            {
                return InlineForm(owner, property, inline.Resource);
            }

            if (!property.LeadingComments.IsEmpty || !property.TrailingComments.IsEmpty)
            {
                return PropForm(owner.Tag, property, UntypedText(property.Value));
            }

            if (property.GodotName == ScriptName)
            {
                return Written(owner.Tag, property, ScriptAttribute, XscnTreeReader.ObjectType);
            }

            string? name = names.AttributeName(owner.Class, property.GodotName);
            return name is null || owner.Reserved.Contains(name) || ReadsAsSignal(owner, name)
                ? PropForm(owner.Tag, property, UntypedText(property.Value))
                : Written(owner.Tag, property, name, names.Resolve(owner.Class, name).Type);
        }

        private bool ReadsAsSignal(PropertyOwner owner, string name) =>
            owner.IsNode && owner.Class is { } cls && XscnSignalNames.SignalAttribute(metadata, cls, name) is not null;

        private PropertyOut InlineForm(PropertyOwner owner, XscnProperty property, XscnResource resource)
        {
            string name =
                names.AttributeName(owner.Class, property.GodotName)
                ?? throw new ArgumentException(
                    $"The property '{property.GodotName}' holds an inline resource but has no attribute name to make a property element of; "
                        + "put the resource in x:Resources."
                );
            var element = new XmlTag(owner.Tag + "." + name, [], [.. InlineItems(resource), .. Comments(property.TrailingComments)]);
            return new PropertyOut(property.GodotName, null, [.. Comments(property.LeadingComments), element]);
        }

        // An attribute text holding a character XML cannot hold has no escape there, so the property becomes an x:Prop.
        private static PropertyOut Written(string tag, XscnProperty property, string name, XscnValueType type) =>
            XscnValueText.Write(property.Value, type) switch
            {
                XscnAttributeText text when FirstForbidden(text.Text) < 0 => new PropertyOut(property.GodotName, new XmlAttr(name, text.Text), []),
                XscnAttributeText => PropForm(tag, property, UntypedText(property.Value)),
                XscnNeedsProp prop => PropForm(tag, property, prop.GodotText),
                var other => throw new InvalidOperationException($"Unexpected write result {other}."),
            };

        private static string UntypedText(XscnValue value) =>
            XscnValueText.Write(value, XscnValueType.Unknown) switch
            {
                XscnAttributeText text => text.Text,
                XscnNeedsProp prop => prop.GodotText,
                var other => throw new InvalidOperationException($"Unexpected write result {other}."),
            };

        private static PropertyOut PropForm(string tag, XscnProperty property, string godotText)
        {
            string value = EscapeForbidden(godotText, $"The property '{property.GodotName}'", "<" + tag + ">");
            var element = new XmlTag(FormatPrefix + ":Prop", [new XmlAttr("Name", property.GodotName), new XmlAttr("Value", value)], []);
            IReadOnlyList<XmlItem> items = [.. Comments(property.LeadingComments), element, .. Comments(property.TrailingComments)];
            return new PropertyOut(property.GodotName, null, items);
        }

        // Name and the other head attributes come before these: Script, then property and signal attributes by name.
        private static IEnumerable<XmlAttr> OrderedAttributes(List<PropertyOut> properties, List<XmlAttr> signals)
        {
            List<XmlAttr> attributes = [.. properties.Select(p => p.Attribute).OfType<XmlAttr>()];
            IEnumerable<XmlAttr> rest = attributes.Where(a => a.Name != ScriptAttribute).Concat(signals).OrderBy(a => a.Name, StringComparer.Ordinal);
            return [.. attributes.Where(a => a.Name == ScriptAttribute), .. rest];
        }

        private static IEnumerable<XmlItem> PropertyElements(List<PropertyOut> properties) =>
            properties.Where(p => p.Attribute is null).OrderBy(p => p.GodotName, StringComparer.Ordinal).SelectMany(p => p.Element);

        // The leading run of connections written as signal attributes. The reader takes attribute connections first and in
        // name order, so the run stops at the first one that is not plain, repeats a name or would sort before the last.
        private (List<XmlAttr> Attributes, List<XscnConnection> Elements) SplitConnections(
            XscnNodeElement element,
            GodotClassInfo? cls,
            IReadOnlySet<string> taken
        )
        {
            List<XmlAttr> attributes = [];
            foreach (XscnConnection connection in element is XscnNode && cls is not null ? element.Connections : [])
            {
                string? name = SignalAttributeName(cls!, connection, taken);
                if (name is null || (attributes.Count > 0 && string.CompareOrdinal(name, attributes[^1].Name) <= 0))
                {
                    break;
                }

                attributes.Add(new XmlAttr(name, connection.Method));
            }

            return (attributes, [.. element.Connections.Skip(attributes.Count)]);
        }

        private string? SignalAttributeName(GodotClassInfo cls, XscnConnection connection, IReadOnlySet<string> taken)
        {
            if (!IsPlain(connection))
            {
                return null;
            }

            string? name = XscnSignalNames.OnClass(metadata, cls, connection.Signal);
            return name is null || taken.Contains(name) || metadata.FindPropertyByCSharpName(cls, name) is not null ? null : name;
        }

        // Received by the root with Persist alone, nothing bound, no id paths, and no comment to keep before it.
        private static bool IsPlain(XscnConnection connection) =>
            connection.To == "."
            && connection.Flags == XscnTreeReader.Persist
            && connection.Binds.IsEmpty
            && connection.Unbinds == 0
            && connection.FromUidPath.IsEmpty
            && connection.ToUidPath.IsEmpty
            && connection.LeadingComments.IsEmpty;

        private XmlTag ConnectTag(GodotClassInfo? cls, XscnConnection connection)
        {
            (string attribute, string value) = XscnSignalNames.ConnectAttribute(metadata, cls, connection.Signal);
            return new(
                FormatPrefix + ":Connect",
                [
                    .. Present(
                        (attribute, value),
                        ("To", connection.To == "." ? null : connection.To),
                        ("Method", connection.Method),
                        ("Flags", FlagsText(connection.Flags)),
                        ("Binds", BindsText(connection.Binds)),
                        ("Unbinds", connection.Unbinds == 0 ? null : Itos(connection.Unbinds)),
                        ("FromUidPath", IdList(connection.FromUidPath)),
                        ("ToUidPath", IdList(connection.ToUidPath))
                    ),
                ],
                []
            );
        }

        private static string? BindsText(ImmutableArray<GodotValue> binds) =>
            binds.IsEmpty ? null : EscapeForbidden(GodotText(new GodotArray(binds, null)), "The Binds attribute", "<x:Connect>");

        // Persist is implied: the other flags by name, or the integer when Persist is absent or a bit has no name.
        private string? FlagsText(int flags)
        {
            const int Persist = XscnTreeReader.Persist;
            if (flags == Persist)
            {
                return null;
            }

            XscnEnumNames names = XscnSignalNames.ConnectFlags(metadata);
            if ((flags & Persist) == 0 || names.Names.IsEmpty)
            {
                return Itos(flags);
            }

            string named = names.Write(flags & ~Persist);
            return long.TryParse(named, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? Itos(flags) : named;
        }
    }
}
