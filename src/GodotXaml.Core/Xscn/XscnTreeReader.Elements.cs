using System.Collections.Frozen;
using System.Collections.Immutable;
using GodotXaml.Core.Diagnostics;
using Microsoft.Language.Xml;

namespace GodotXaml.Core.Xscn;

/// <summary>The elements: the root, nodes, overrides, resources, property elements and the format's own elements.</summary>
internal sealed partial class XscnTreeReader
{
    // The format's elements that sit among a node's or a resource's content, by local name; the names are the
    // vocabulary's, less x:Uid, which sits only in x:Uids.
    private static readonly FrozenDictionary<string, ChildReader> FormatChildren = XscnFormatVocabulary
        .Elements.Select(name => (Name: name, Read: FormatChild(name)))
        .Where(child => child.Read is not null)
        .ToFrozenDictionary(child => child.Name, child => child.Read!, StringComparer.Ordinal);

    private delegate void ChildReader(XscnTreeReader reader, ElementParts parent, IXmlElementSyntax element);

    private static ChildReader? FormatChild(string name) =>
        name switch
        {
            "Override" => (reader, parent, element) => reader.AddOverride(parent, element),
            "Instance" => (reader, parent, element) => reader.AddInstance(parent, element),
            "Connect" => (reader, parent, element) => reader.AddConnection(parent, element),
            "Prop" => (reader, parent, element) => reader.AddProp(parent, element),
            "Uids" => (reader, parent, element) => reader.AddUidTable(parent, element),
            "Resources" => (reader, parent, element) => reader.AddResourceTable(parent, element),
            "Uid" => null,
            _ => throw new InvalidOperationException($"The vocabulary lists x:{name}, which the reader cannot read."),
        };

    private XscnNode ReadRootNode(IXmlElementSyntax root)
    {
        CheckPlaceholderRoot(root);
        TagKind kind = KindOf(root);
        if (kind == TagKind.Format && LocalName(root) == "Instance")
        {
            return ReadNode(new ElementParts(root, ElementKind.Instance, null, isRoot: true), null, []);
        }

        XscnType type = kind is TagKind.Property or TagKind.Format ? MisplacedRoot(root, "node") : NodeType(root, kind);
        return ReadNode(new ElementParts(root, ElementKind.Node, ClassOf(type), isRoot: true), type, []);
    }

    // Godot's text loader refuses a scene whose first node is an instance placeholder (resource_format_text.cpp:243-253);
    // the element is read as any other, so the file gives one error.
    private void CheckPlaceholderRoot(IXmlElementSyntax root)
    {
        foreach (AttributeText attribute in FirstOfEachName(root).Select(Attribute))
        {
            if (attribute.LocalName == "InstancePlaceholder" && attribute.Prefix == formatPrefix)
            {
                Report(
                    XscnCodes.PlaceholderRoot,
                    "The root cannot be an instance placeholder; Godot loads no scene whose first node is one.",
                    attribute.Span
                );
                return;
            }
        }
    }

    private XscnResource ReadRootResource(IXmlElementSyntax root)
    {
        XscnType type = KindOf(root) == TagKind.Class ? ClassTag(root) : MisplacedRoot(root, "resource tagged with its class");
        CheckIsResource(root, type);
        return ReadResource(new ElementParts(root, ElementKind.Resource, ClassOf(type), isRoot: true), type, []);
    }

    // Only a Resource can be held where a resource goes; the element is still read, so the mistake gives one error.
    private void CheckIsResource(IXmlElementSyntax element, XscnType type)
    {
        if (ClassOf(type) is { } cls && !metadata.Inherits(cls, "Resource"))
        {
            Report(
                XscnCodes.NotAResource,
                $"<{FullName(element)}> is a {cls.Name}, not a Resource; only a resource can be held here.",
                NameSpan(element)
            );
        }
    }

    private XscnUnknownType MisplacedRoot(IXmlElementSyntax root, string expected)
    {
        Report(XscnCodes.MisplacedElement, $"<{FullName(root)}> cannot be the root, which is a {expected}.", NameSpan(root));
        return new XscnUnknownType(FullName(root));
    }

    private XscnType NodeType(IXmlElementSyntax element, TagKind kind) =>
        kind switch
        {
            TagKind.Class => ClassTag(element),
            TagKind.Scene => SceneTag(element),
            _ => UndeclaredTag(element),
        };

    // An instance's type comes from its Scene attribute, read with the others.
    private XscnNode ReadNode(ElementParts parts, XscnType? type, ImmutableArray<string> leading)
    {
        ReadAttributes(parts);
        ReadContent(parts);
        CheckName(parts);
        var node = new XscnNode
        {
            Type = type ?? InstanceType(parts),
            Name = parts.Name ?? "",
            Unique = parts.Unique,
        };
        return (XscnNode)WithNodeParts(node, parts, leading);
    }

    // Name="" and Name="%" name nothing, as a missing Name does.
    private void CheckName(ElementParts parts)
    {
        if (string.IsNullOrEmpty(parts.Name))
        {
            string problem = parts.Name is null ? "has no Name" : "has an empty Name";
            Report(XscnCodes.MissingName, $"<{FullName(parts.Syntax)}> {problem}; every node needs one.", NameSpan(parts.Syntax));
        }
    }

    private XscnType InstanceType(ElementParts parts)
    {
        if (parts.Scene is { } scene)
        {
            return new XscnSceneType(scene);
        }

        Report(XscnCodes.MissingAttribute, "<x:Instance> needs Scene=\"res://…\".", NameSpan(parts.Syntax));
        return new XscnUnknownType(FullName(parts.Syntax));
    }

    private XscnOverride ReadOverride(ElementParts parts, ImmutableArray<string> leading)
    {
        ReadAttributes(parts);
        ReadContent(parts);
        if (parts.Path is null)
        {
            Report(XscnCodes.MissingAttribute, "<x:Override> needs Path=\"…\", the node's path inside the instance.", NameSpan(parts.Syntax));
        }

        return (XscnOverride)WithNodeParts(new XscnOverride { Path = parts.Path ?? "" }, parts, leading);
    }

    private static XscnNodeElement WithNodeParts(XscnNodeElement element, ElementParts parts, ImmutableArray<string> leading) =>
        element with
        {
            Properties = [.. parts.Properties],
            Connections = [.. parts.Connections],
            Children = [.. parts.Children],
            Index = parts.Index,
            Owner = parts.Owner,
            Editable = parts.Editable,
            Groups = parts.Groups,
            InstancePlaceholder = parts.InstancePlaceholder,
            NodePaths = parts.NodePaths,
            ParentIdPath = parts.ParentIdPath,
            OwnerUidPath = parts.OwnerUidPath,
            UniqueId = parts.UniqueId,
            LeadingComments = leading,
            TrailingComments = CommentList(parts.Pending),
            Span = SpanOf(parts.Syntax.AsNode),
        };

    private XscnResource ReadResource(ElementParts parts, XscnType type, ImmutableArray<string> leading)
    {
        ReadAttributes(parts);
        ReadContent(parts);
        return new XscnResource
        {
            Type = type,
            Key = parts.Key,
            Properties = [.. parts.Properties],
            LeadingComments = leading,
            TrailingComments = CommentList(parts.Pending),
            Span = SpanOf(parts.Syntax.AsNode),
        };
    }

    private void ReadContent(ElementParts parts)
    {
        bool commentInStartTag = CommentInStartTag(parts.Syntax);
        foreach (SyntaxNode item in parts.Syntax.Content.Skip(commentInStartTag ? 1 : 0))
        {
            if (item is IXmlElementSyntax element)
            {
                ReadChild(parts, element);
            }
            else
            {
                ReadLooseItem(item, parts.Pending);
            }
        }
    }

    // The XML parser ends a start tag at a comment inside it, as if '>' were missing, and makes the comment content.
    private bool CommentInStartTag(IXmlElementSyntax syntax)
    {
        if (
            syntax is not XmlElementSyntax { StartTag.GreaterThanToken.Width: 0 } element
            || element.Content.FirstOrDefault() is not XmlCommentSyntax comment
        )
        {
            return false;
        }

        Report(XscnCodes.CommentInStartTag, "A comment cannot sit inside a start tag; move it before the element.", SpanOf(comment));
        return true;
    }

    private void ReadChild(ElementParts parent, IXmlElementSyntax element)
    {
        TagKind kind = KindOf(element);
        switch (kind)
        {
            case TagKind.Property:
                ReadPropertyElement(parent, element);
                break;
            case TagKind.Format:
                ReadFormatChild(parent, element);
                break;
            default:
                AddNode(parent, element, kind);
                break;
        }
    }

    private void ReadFormatChild(ElementParts parent, IXmlElementSyntax element)
    {
        if (FormatChildren.TryGetValue(LocalName(element), out ChildReader? read))
        {
            read(this, parent, element);
        }
        else
        {
            Report(XscnCodes.UnknownFormatElement, $"<{FullName(element)}> is not an element of the format.", NameSpan(element));
        }
    }

    private static ImmutableArray<string> TakePending(ElementParts parts)
    {
        ImmutableArray<string> leading = CommentList(parts.Pending);
        parts.Pending.Clear();
        return leading;
    }

    // No comments is the model's own empty array, so a record without comments equals one built without them.
    private static ImmutableArray<string> CommentList(IEnumerable<string> comments)
    {
        ImmutableArray<string> list = [.. comments];
        return list.IsEmpty ? [] : list;
    }

    private bool RequireNode(ElementParts parent, IXmlElementSyntax element)
    {
        if (parent.IsNode)
        {
            return true;
        }

        Report(XscnCodes.MisplacedElement, $"<{FullName(element)}> belongs inside a node; a resource holds only properties.", NameSpan(element));
        return false;
    }

    private void AddNode(ElementParts parent, IXmlElementSyntax element, TagKind kind)
    {
        if (RequireNode(parent, element))
        {
            XscnType type = NodeType(element, kind);
            parent.Children.Add(ReadNode(new ElementParts(element, ElementKind.Node, ClassOf(type), isRoot: false), type, TakePending(parent)));
        }
    }

    private void AddInstance(ElementParts parent, IXmlElementSyntax element)
    {
        if (RequireNode(parent, element))
        {
            parent.Children.Add(ReadNode(new ElementParts(element, ElementKind.Instance, null, isRoot: false), null, TakePending(parent)));
        }
    }

    private void AddOverride(ElementParts parent, IXmlElementSyntax element)
    {
        if (RequireNode(parent, element))
        {
            parent.Children.Add(ReadOverride(new ElementParts(element, ElementKind.Override, null, isRoot: false), TakePending(parent)));
        }
    }

    private bool CheckOnRoot(ElementParts parent, IXmlElementSyntax element)
    {
        if (!parent.IsRoot)
        {
            Report(XscnCodes.MisplacedElement, $"<{FullName(element)}> belongs on the root element.", NameSpan(element));
        }

        return parent.IsRoot;
    }

    // x:Uids is read before the rest of the root, so its scene tags know their extensions; here it only takes the
    // comments before it. Elsewhere it is out of place.
    private void AddUidTable(ElementParts parent, IXmlElementSyntax element)
    {
        if (CheckOnRoot(parent, element) && IsFirstTable(element, ref uidTableSeen))
        {
            uidsLeading.AddRange(TakePending(parent));
        }
    }

    // A second table on the root is reported and skipped, its content and the comments inside it unread; comments before
    // it stay pending, to lead the next element.
    private bool IsFirstTable(IXmlElementSyntax element, ref bool seen)
    {
        if (seen)
        {
            Report(XscnCodes.SecondTable, $"Only one <{FullName(element)}> table is allowed; this one is not read.", NameSpan(element));
            return false;
        }

        seen = true;
        return true;
    }

    private void AddResourceTable(ElementParts parent, IXmlElementSyntax element)
    {
        if (CheckOnRoot(parent, element) && IsFirstTable(element, ref resourceTableSeen))
        {
            resourcesLeading.AddRange(TakePending(parent));
            ExpectNoAttributes(element);
            resources.AddRange(ReadResourceList(element, ElementKind.TableEntry, resourcesTrailing));
        }
    }

    // <Owner.Property>, the owner being the parent's own tag, holding the one resource the property is set to; comments
    // after the resource trail the property. Without a resource, the comments before and inside it go back to the parent,
    // to lead its next element.
    private void ReadPropertyElement(ElementParts parent, IXmlElementSyntax element)
    {
        string fullName = FullName(element);
        int dot = fullName.IndexOf('.', StringComparison.Ordinal);
        CheckOwner(parent, element, fullName[..dot]);
        ExpectNoAttributes(element);
        ImmutableArray<string> leading = TakePending(parent);
        List<string> trailing = [];
        List<XscnResource> held = ReadResourceList(element, ElementKind.Resource, trailing);
        if (NamesNoProperty(fullName[(dot + 1)..]))
        {
            Report(
                XscnCodes.InvalidPropertyElement,
                $"<{fullName}> names no property; write <{FullName(parent.Syntax)}.Property>.",
                NameSpan(element)
            );
            parent.Pending.AddRange([.. leading, .. trailing]);
            return;
        }

        CheckHoldsOne(element, held.Count);
        if (held.Count == 0)
        {
            parent.Pending.AddRange([.. leading, .. trailing]);
            return;
        }

        XscnResolvedProperty property = names.Resolve(parent.Class, fullName[(dot + 1)..]);
        parent.Properties.Add(
            new XscnProperty(property.GodotName, new XscnInline(held[0])) { LeadingComments = leading, TrailingComments = CommentList(trailing) }
        );
    }

    // <Owner.Property> with a segment left empty names no property, the same as a property element with no name at all.
    private static bool NamesNoProperty(string name) => name.Length == 0 || name.Split('.').Any(segment => segment.Length == 0);

    private void CheckOwner(ElementParts parent, IXmlElementSyntax element, string owner)
    {
        string expected = FullName(parent.Syntax);
        if (owner != expected)
        {
            Report(XscnCodes.InvalidPropertyElement, $"<{FullName(element)}> must begin with its parent's tag, {expected}.", NameSpan(element));
        }
    }

    // A child that is not a resource has been reported already; the count is checked only when every child is one.
    private void CheckHoldsOne(IXmlElementSyntax element, int held)
    {
        if (held != 1 && element.Elements.All(e => KindOf(e) == TagKind.Class))
        {
            Report(XscnCodes.InvalidPropertyElement, $"<{FullName(element)}> must hold exactly one resource.", NameSpan(element));
        }
    }

    // The resources a property element or x:Resources holds. Comments before a child that is not a resource lead the next
    // resource; those after the last resource are left in pending.
    private List<XscnResource> ReadResourceList(IXmlElementSyntax holder, ElementKind kind, List<string> pending)
    {
        List<XscnResource> found = [];
        foreach (SyntaxNode item in holder.Content)
        {
            if (item is not IXmlElementSyntax child)
            {
                ReadLooseItem(item, pending);
            }
            else if (ListedResource(child, kind, CommentList(pending)) is { } resource)
            {
                found.Add(resource);
                pending.Clear();
            }
        }

        return found;
    }

    private XscnResource? ListedResource(IXmlElementSyntax element, ElementKind kind, ImmutableArray<string> leading)
    {
        if (KindOf(element) != TagKind.Class)
        {
            string code = kind == ElementKind.TableEntry ? XscnCodes.MisplacedElement : XscnCodes.InvalidPropertyElement;
            Report(code, $"<{FullName(element)}> is not a resource; a resource is tagged with its class.", NameSpan(element));
            return null;
        }

        XscnType type = ClassTag(element);
        CheckIsResource(element, type);
        var parts = new ElementParts(element, kind, ClassOf(type), isRoot: false);
        XscnResource resource = ReadResource(parts, type, leading);
        return kind != ElementKind.TableEntry || HasNewKey(parts) ? resource : null;
    }

    // A table entry needs x:Key, and a key no earlier entry has; an entry with no key, an empty one, or a key that is taken
    // is dropped, the first of a repeated key kept.
    private bool HasNewKey(ElementParts parts)
    {
        if (parts.Key is null)
        {
            Report(XscnCodes.MissingAttribute, $"<{FullName(parts.Syntax)}> in x:Resources needs x:Key.", NameSpan(parts.Syntax));
            return true;
        }

        if (parts.Key.Length == 0)
        {
            Report(
                XscnCodes.MissingAttribute,
                $"<{FullName(parts.Syntax)}> in x:Resources has an empty x:Key; every entry needs one.",
                parts.KeyAttributeSpan
            );
            return false;
        }

        if (resourceKeys.Add(parts.Key))
        {
            return true;
        }

        Report(XscnCodes.RepeatedKey, $"The key '{parts.Key}' is already used in x:Resources; this resource is not read.", parts.KeySpan);
        return false;
    }

    // Comments before an entry lead it, kept past an entry that is not read; those after the last entry trail the table.
    private List<XscnUid> ReadUidTable(IXmlElementSyntax table)
    {
        ExpectNoAttributes(table);
        List<XscnUid> found = [];
        List<string> pending = [];
        foreach (SyntaxNode item in table.Content)
        {
            if (item is not IXmlElementSyntax entry)
            {
                ReadLooseItem(item, pending);
            }
            else if (UidEntry(entry) is { } uid)
            {
                found.Add(uid with { LeadingComments = CommentList(pending) });
                pending.Clear();
            }
        }

        uidsTrailing.AddRange(pending);
        return found;
    }

    private XscnUid? UidEntry(IXmlElementSyntax entry)
    {
        if (IsFormat(entry, "Uid"))
        {
            return ReadUid(entry);
        }

        Report(XscnCodes.MisplacedElement, $"<{FullName(entry)}> does not belong in x:Uids, which holds x:Uid entries.", NameSpan(entry));
        return null;
    }

    private XscnUid? ReadUid(IXmlElementSyntax entry)
    {
        Dictionary<string, AttributeText> attributes = FixedAttributes(entry, XscnFormatVocabulary.UidAttributes);
        ExpectNoElements(entry);
        AttributeText? path = Required(entry, attributes, "Path");
        AttributeText? value = Required(entry, attributes, "Value");
        return path is null || value is null ? null : new XscnUid(path.Value, value.Value);
    }

    private void ExpectNoElements(IXmlElementSyntax element)
    {
        foreach (IXmlElementSyntax child in element.Elements)
        {
            Report(XscnCodes.MisplacedElement, $"<{FullName(element)}> holds no elements.", NameSpan(child));
        }
    }
}
