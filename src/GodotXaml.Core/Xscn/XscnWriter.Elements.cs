using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Xscn;

/// <summary>The elements: the root and its tables, nodes, overrides and resources.</summary>
public static partial class XscnWriter
{
    private const string FormatPrefix = "x";

    // A resource's only attribute that is not a property is Script.
    private static readonly FrozenSet<string> ResourceReserved = new[] { ScriptAttribute }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Turns one document into the elements and comments of its text.</summary>
    /// <param name="document">The document.</param>
    /// <param name="metadata">The metadata it is written with.</param>
    /// <param name="names">The attribute names of that metadata.</param>
    private sealed partial class Session(XscnDocument document, GodotMetadata metadata, XscnPropertyNames names)
    {
        // The uid paths in the order the table is written, which is the order the reader searches them in.
        private readonly List<string> uidPaths = [.. document.Uids.Select(u => u.Path).Order(StringComparer.Ordinal)];

        // Each scene namespace's prefix as written: its own, or a free one when it is the format's.
        private readonly FrozenDictionary<string, string> prefixes = WrittenPrefixes(document.Namespaces);

        /// <summary>The root's leading comments, then the root.</summary>
        public IEnumerable<XmlItem> DocumentItems()
        {
            XmlTag root = document.Root switch
            {
                XscnNodeElement node => NodeTag(node, isRoot: true, parentIsContainer: false),
                XscnResource resource => ResourceTag(resource, isRoot: true),
                _ => throw new ArgumentException($"The root is a {document.Root.GetType().Name}, not a node or a resource.", nameof(document)),
            };
            return [.. Comments(document.Root.LeadingComments), root];
        }

        // A scene namespace may not take the format's prefix, so one that has it is given x1, x2, …, the first not taken.
        private static FrozenDictionary<string, string> WrittenPrefixes(ImmutableArray<XscnNamespace> namespaces)
        {
            HashSet<string> taken = [.. namespaces.Select(n => n.Prefix), FormatPrefix];
            Dictionary<string, string> written = new(StringComparer.Ordinal);
            foreach (XscnNamespace scope in namespaces)
            {
                written[scope.Prefix] = scope.Prefix == FormatPrefix ? FreePrefix(taken) : scope.Prefix;
            }

            return written.ToFrozenDictionary(StringComparer.Ordinal);
        }

        private static string FreePrefix(HashSet<string> taken)
        {
            int n = 1;
            while (!taken.Add(FormatPrefix + Itos(n)))
            {
                n++;
            }

            return FormatPrefix + Itos(n);
        }

        private static IEnumerable<XmlItem> Comments(ImmutableArray<string> comments) => comments.Select(c => (XmlItem)new XmlComment(c));

        private static string Itos(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static GodotClassInfo? ClassOf(XscnType type) => (type as XscnClassType)?.Class;

        private static GodotClassInfo? ClassOf(XscnNodeElement element) => element is XscnNode node ? ClassOf(node.Type) : null;

        private XmlTag NodeTag(XscnNodeElement element, bool isRoot, bool parentIsContainer)
        {
            GodotClassInfo? cls = ClassOf(element);
            (string tag, List<XmlAttr> head) = Head(element);
            HashSet<string> reserved = [.. head.Select(a => a.Name), ScriptAttribute];
            List<PropertyOut> properties = Properties(
                element.Properties,
                new PropertyOwner(tag, cls, reserved, element is XscnNode, parentIsContainer, element.InstancePlaceholder is not null)
            );
            reserved.UnionWith(properties.Select(p => p.Attribute?.Name).OfType<string>());
            (List<XmlAttr> signals, List<XscnConnection> connects) = SplitConnections(element, cls, reserved);
            bool childrenInContainer = cls is not null && metadata.Inherits(cls, "Container");
            List<XmlAttr> attributes =
            [
                .. RootAttributes(isRoot),
                .. head,
                .. OrderedAttributes(properties, signals),
                .. FormatAttributes(element, tag),
            ];
            List<XmlItem> content =
            [
                .. RootTables(isRoot),
                .. PropertyElements(properties),
                .. connects.SelectMany(c => (IEnumerable<XmlItem>)[.. Comments(c.LeadingComments), ConnectTag(cls, c)]),
                .. element.Children.SelectMany(c => (IEnumerable<XmlItem>)[.. Comments(c.LeadingComments), NodeTag(c, false, childrenInContainer)]),
                .. Comments(element.TrailingComments),
            ];
            return new XmlTag(tag, attributes, content);
        }

        private XmlTag ResourceTag(XscnResource resource, bool isRoot)
        {
            string tag = TypeName(resource.Type);
            List<PropertyOut> properties = Properties(
                resource.Properties,
                new PropertyOwner(tag, ClassOf(resource.Type), ResourceReserved, false, false, false)
            );
            List<XmlAttr> key = resource.Key is { } value ? [new XmlAttr(FormatPrefix + ":Key", value)] : [];
            List<XmlAttr> attributes = [.. RootAttributes(isRoot), .. key, .. OrderedAttributes(properties, [])];
            List<XmlItem> content = [.. RootTables(isRoot), .. PropertyElements(properties), .. Comments(resource.TrailingComments)];
            return new XmlTag(tag, attributes, content);
        }

        // An inline resource: its leading comments and its element, inside its property element.
        private IEnumerable<XmlItem> InlineItems(XscnResource resource) => [.. Comments(resource.LeadingComments), ResourceTag(resource, false)];

        private static string TypeName(XscnType type) =>
            type switch
            {
                XscnClassType cls => cls.Class.CSharpName,
                XscnUnknownType unknown => unknown.Name,
                _ => throw new ArgumentException($"A resource cannot be tagged with a scene ({type}).", nameof(type)),
            };

        // The tag and the attributes before Script: Path on an override; Name on a node, and Scene on an x:Instance.
        private (string Tag, List<XmlAttr> Head) Head(XscnNodeElement element)
        {
            if (element is XscnOverride change)
            {
                return (FormatPrefix + ":Override", [new XmlAttr("Path", change.Path)]);
            }

            var node = (XscnNode)element;
            var name = new XmlAttr("Name", (node.Unique ? "%" : "") + node.Name);
            return node.Type switch
            {
                XscnSceneType scene when SceneTag(scene.Path) is { } tag => (tag, [name]),
                XscnSceneType scene => (FormatPrefix + ":Instance", [name, new XmlAttr("Scene", scene.Path)]),
                _ => (TypeName(node.Type), [name]),
            };
        }

        // {prefix}:{file name without extension}, when a scene namespace names the path's folder and the reader would
        // recover the same path from the tag; null when it would not.
        private string? SceneTag(string path)
        {
            int slash = path.LastIndexOf('/');
            string folder = path[..(slash + 1)];
            string file = path[(slash + 1)..];
            int dot = file.LastIndexOf('.');
            string local = dot > 0 ? file[..dot] : file;
            XscnNamespace? scope = document.Namespaces.FirstOrDefault(n => n.Folder == folder);
            return scope is not null && IsTagName(local) && RecoveredPath(folder + local) == path ? prefixes[scope.Prefix] + ":" + local : null;
        }

        // A tag's local name is an XML name without a '.', which would make it a property element.
        private static bool IsTagName(string name) =>
            name.Length > 0 && XmlConvert.IsStartNCNameChar(name[0]) && name.Skip(1).All(XmlConvert.IsNCNameChar) && !name.Contains('.');

        // The path the reader gives a scene tag: the first uid path with the tag's stem and an extension, else .xscn.
        private string RecoveredPath(string stem) => uidPaths.FirstOrDefault(p => XscnTreeReader.IsStemOf(stem, p)) ?? stem + ".xscn";

        // The root's own attributes, before the node's or the resource's: xmlns:x, the scene namespaces, then x:Godot with
        // the version of the metadata this writes with (not the document's marker). The marker is a file-level attribute,
        // so it sits with the namespace declarations rather than with the x: attributes after the properties.
        private IEnumerable<XmlAttr> RootAttributes(bool isRoot) =>
            isRoot
                ?
                [
                    new XmlAttr("xmlns:" + FormatPrefix, XscnParser.FormatNamespace),
                    .. document.Namespaces.Select(n => new XmlAttr("xmlns:" + prefixes[n.Prefix], n.Folder)),
                    new XmlAttr(FormatPrefix + ":" + XscnFormatVocabulary.Godot, metadata.Version),
                ]
                : [];

        private IEnumerable<XmlItem> RootTables(bool isRoot) =>
            isRoot
                ?
                [
                    .. Table(
                        "Uids",
                        document.UidsLeadingComments,
                        [.. document.Uids.OrderBy(u => u.Path, StringComparer.Ordinal).SelectMany(UidItems)],
                        document.UidsTrailingComments
                    ),
                    .. Table(
                        "Resources",
                        document.ResourcesLeadingComments,
                        [.. document.Resources.SelectMany(InlineItems)],
                        document.ResourcesTrailingComments
                    ),
                ]
                : [];

        // A table with no entries is left out, unless it carries comments.
        private static IEnumerable<XmlItem> Table(string name, ImmutableArray<string> leading, List<XmlItem> entries, ImmutableArray<string> trailing)
        {
            if (entries.Count == 0 && leading.IsEmpty && trailing.IsEmpty)
            {
                return [];
            }

            return [.. Comments(leading), new XmlTag(FormatPrefix + ":" + name, [], [.. entries, .. Comments(trailing)])];
        }

        private static IEnumerable<XmlItem> UidItems(XscnUid uid) =>
            [.. Comments(uid.LeadingComments), new XmlTag(FormatPrefix + ":Uid", [new XmlAttr("Path", uid.Path), new XmlAttr("Value", uid.Uid)], [])];

        // The x: attributes a node carries, in ordinal order with x:UniqueId last, each only when the model has it.
        private static IEnumerable<XmlAttr> FormatAttributes(XscnNodeElement element, string tag) =>
            Present(
                    ("Editable", element.Editable ? "True" : null),
                    ("Groups", NameList(element.Groups, "x:Groups", tag)),
                    ("Index", element.Index is { } index ? Itos(index) : null),
                    ("InstancePlaceholder", element.InstancePlaceholder),
                    ("NodePaths", NameList(element.NodePaths, "x:NodePaths", tag)),
                    ("Owner", element.Owner),
                    ("OwnerUidPath", IdList(element.OwnerUidPath)),
                    ("ParentIdPath", IdList(element.ParentIdPath)),
                    ("UniqueId", element.UniqueId is { } id ? Itos(id) : null)
                )
                .Select(a => a with { Name = FormatPrefix + ":" + a.Name });

        private static IEnumerable<XmlAttr> Present(params (string Name, string? Value)[] attributes) =>
            attributes.Where(a => a.Value is not null).Select(a => new XmlAttr(a.Name, a.Value!));

        // Names joined with ", "; Godot array text when a name would not survive the split (a comma, edge whitespace, an
        // empty name, a first name that reads as an array, or a character XML cannot hold, which the array text escapes).
        private static string? NameList(ImmutableArray<string> list, string attribute, string tag)
        {
            if (list.IsEmpty)
            {
                return null;
            }

            if (list.All(IsPlainName) && !list[0].StartsWith('['))
            {
                return string.Join(", ", list);
            }

            string text = GodotText(new GodotArray([.. list.Select(n => (GodotValue)new GodotString(n))], null));
            return EscapeForbidden(text, $"The {attribute} attribute", "<" + tag + ">");
        }

        private static bool IsPlainName(string name) =>
            name.Length > 0 && !name.Contains(',') && !char.IsWhiteSpace(name[0]) && !char.IsWhiteSpace(name[^1]) && FirstForbidden(name) < 0;

        private static string? IdList(ImmutableArray<int> ids) => ids.IsEmpty ? null : string.Join(", ", ids.Select(Itos));
    }
}
