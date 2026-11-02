using System.Collections.Immutable;
using System.Globalization;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;
using Microsoft.Language.Xml;
using TextSpan = GodotXaml.Core.Diagnostics.TextSpan;

namespace GodotXaml.Core.Xscn;

/// <summary>The format's elements with fixed attributes: <c>x:Prop</c> and <c>x:Connect</c>, and the parts both build on.</summary>
internal sealed partial class XscnTreeReader
{
    private static readonly string[] ConnectAttributes = ["Signal", "To", "Method", "Flags", "Binds", "Unbinds", "FromUidPath", "ToUidPath"];

    // <x:Prop Name="…" Value="…" />: the Godot name verbatim, the value untyped Godot value text.
    private void AddProp(ElementParts parent, IXmlElementSyntax element)
    {
        Dictionary<string, AttributeText> attributes = FixedAttributes(element, ["Name", "Value"]);
        ExpectNoElements(element);
        AttributeText? name = Required(element, attributes, "Name");
        AttributeText? value = Required(element, attributes, "Value");
        if (name is not null && value is not null)
        {
            parent.Properties.Add(new XscnProperty(name.Value, ReadValue(value, XscnValueType.Unknown)) { LeadingComments = TakePending(parent) });
        }
    }

    private void AddConnection(ElementParts parent, IXmlElementSyntax element)
    {
        if (!RequireNode(parent, element))
        {
            return;
        }

        Dictionary<string, AttributeText> attributes = FixedAttributes(element, ConnectAttributes);
        ExpectNoElements(element);
        AttributeText? signal = Required(element, attributes, "Signal");
        AttributeText? method = Required(element, attributes, "Method");
        if (signal is not null && method is not null)
        {
            XscnConnection connection = ReadConnection(attributes, SignalName(parent.Class, signal.Value), method.Value);
            parent.Connections.Add(connection with { LeadingComments = TakePending(parent) });
        }
    }

    private XscnConnection ReadConnection(Dictionary<string, AttributeText> attributes, string signal, string method) =>
        new(
            signal,
            attributes.GetValueOrDefault("To")?.Value ?? ".",
            method,
            ReadConnectFlags(attributes.GetValueOrDefault("Flags")),
            ReadBinds(attributes.GetValueOrDefault("Binds")),
            attributes.GetValueOrDefault("Unbinds") is { } unbinds ? ReadInt(unbinds) ?? 0 : 0,
            attributes.GetValueOrDefault("FromUidPath") is { } from ? ReadIdPath(from) : [],
            attributes.GetValueOrDefault("ToUidPath") is { } to ? ReadIdPath(to) : []
        );

    private string SignalName(GodotClassInfo? cls, string csName) => XscnSignalNames.GodotName(metadata, cls, csName);

    // Flag names add to Persist, which is implied; an integer is the flags as stored, Persist included or not.
    private int ReadConnectFlags(AttributeText? flags)
    {
        if (flags is null)
        {
            return Persist;
        }

        if (int.TryParse(flags.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int stored))
        {
            return stored;
        }

        if (ConnectFlags.Names.IsEmpty)
        {
            Report(XscnCodes.InvalidValue, "this metadata has no ConnectFlags names; write the integer", flags.ValueSpan);
            return Persist;
        }

        if (ConnectFlags.Read(flags.Value) is { } named)
        {
            return (int)named | Persist;
        }

        string expected = string.Join(", ", ConnectFlags.Names.Select(n => n.Name));
        Report(XscnCodes.InvalidValue, $"Expected connect flags joined with commas ({expected}), or an integer.", flags.ValueSpan);
        return Persist;
    }

    private ImmutableArray<GodotValue> ReadBinds(AttributeText? binds)
    {
        if (binds is null)
        {
            return [];
        }

        if (XscnValueText.Read(binds.Value, XscnValueType.Unknown) is XscnScalar { Value: GodotArray array })
        {
            return array.Items;
        }

        Report(XscnCodes.InvalidValue, "Expected the bound arguments as a Godot array ([1, \"a\"]).", binds.ValueSpan);
        return [];
    }

    // The attributes of a format element, which takes only the given unprefixed names; any other is reported.
    private Dictionary<string, AttributeText> FixedAttributes(IXmlElementSyntax element, string[] allowed)
    {
        Dictionary<string, AttributeText> found = new(StringComparer.Ordinal);
        foreach (AttributeText attribute in FirstOfEachName(element).Select(Attribute))
        {
            if (attribute.Prefix.Length == 0 && allowed.Contains(attribute.LocalName, StringComparer.Ordinal))
            {
                found[attribute.LocalName] = attribute;
            }
            else
            {
                UnknownAttribute(element, attribute);
            }
        }

        return found;
    }

    private AttributeText? Required(IXmlElementSyntax element, Dictionary<string, AttributeText> attributes, string name)
    {
        if (attributes.TryGetValue(name, out AttributeText? attribute))
        {
            return attribute;
        }

        Report(XscnCodes.MissingAttribute, $"<{FullName(element)}> needs {name}=\"…\".", NameSpan(element));
        return null;
    }

    private void ExpectNoAttributes(IXmlElementSyntax element)
    {
        foreach (AttributeText attribute in FirstOfEachName(element).Select(Attribute))
        {
            UnknownAttribute(element, attribute);
        }
    }

    /// <summary>What reading an element gathers before the model record is made.</summary>
    /// <param name="syntax">The element.</param>
    /// <param name="kind">What the element is.</param>
    /// <param name="cls">The element's class, or null when it is not known.</param>
    /// <param name="isRoot">Whether the element is the document's root.</param>
    private sealed class ElementParts(IXmlElementSyntax syntax, ElementKind kind, GodotClassInfo? cls, bool isRoot)
    {
        public IXmlElementSyntax Syntax { get; } = syntax;

        public ElementKind Kind { get; } = kind;

        public GodotClassInfo? Class { get; } = cls;

        public bool IsRoot { get; } = isRoot;

        public bool IsNode => Kind is ElementKind.Node or ElementKind.Instance or ElementKind.Override;

        public List<XscnProperty> Properties { get; } = [];

        public List<XscnConnection> Connections { get; } = [];

        public List<XscnNodeElement> Children { get; } = [];

        // Comments read since the last element; they lead the next one, or trail the content at its end.
        public List<string> Pending { get; } = [];

        public string? Name { get; set; }

        public bool Unique { get; set; }

        public string? Scene { get; set; }

        public string? Path { get; set; }

        public string? Key { get; set; }

        // The x:Key value's text between the quotes.
        public TextSpan KeySpan { get; set; }

        public int? Index { get; set; }

        public string? Owner { get; set; }

        public bool Editable { get; set; }

        public ImmutableArray<string> Groups { get; set; } = [];

        public string? InstancePlaceholder { get; set; }

        public ImmutableArray<string> NodePaths { get; set; } = [];

        public ImmutableArray<int> ParentIdPath { get; set; } = [];

        public ImmutableArray<int> OwnerUidPath { get; set; } = [];

        public int? UniqueId { get; set; }
    }

    private enum ElementKind
    {
        Node,
        Instance,
        Override,
        Resource,
        TableEntry,
    }
}
