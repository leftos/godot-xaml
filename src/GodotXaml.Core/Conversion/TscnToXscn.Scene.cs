using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

public static partial class TscnToXscn
{
    private sealed partial class Context
    {
        private const string UniqueNameProperty = "unique_name_in_owner";

        // The class Godot builds for a placeholder at load (packed_scene.cpp:241-257); the .tscn writes no type for it.
        private const string PlaceholderClass = "InstancePlaceholder";

        // Every converted node and override by its path from the root, as a .tscn's parent= and from= give it.
        private readonly Dictionary<string, SceneElement> elements = new(StringComparer.Ordinal);

        // The paths of the nodes that were not converted, whose subtrees are not converted either.
        private readonly HashSet<string> lost = new(StringComparer.Ordinal);

        // A .tscn becomes an .xscn whose elements nest as the nodes' parent= paths say: a node inside an instance goes
        // into an x:Override for its path inside that instance, made when the .tscn has none, and a connection sits on
        // its emitter's element.
        public XscnDocument ConvertScene()
        {
            ImmutableArray<XscnResource> table = Table();
            SceneElement root = AddRoot();
            foreach (TscnNode node in document.Nodes.Skip(1))
            {
                AddNode(node);
            }

            foreach (TscnConnection connection in document.Connections)
            {
                AddConnection(connection);
            }

            foreach (TscnEditable editable in document.Editables)
            {
                MarkEditable(editable);
            }

            XscnNodeElement built = root.Build();
            return new XscnDocument(XscnDocumentKind.Scene, Namespaces(built), Uids(), table, built, metadata.Version);
        }

        private SceneElement AddRoot()
        {
            TscnNode? node = document.Nodes.FirstOrDefault();
            string location = node is null ? "[gd_scene]" : NodeLocation(node);
            XscnType? type = null;
            if (node is not null)
            {
                ReportExtra(node.Extra, location);
                type =
                    node.Instance is { } instance ? SceneType(instance, location, "a Node stands in for the root.") ?? TypeOf("Node", location)
                    : node.Type is { } cls ? TypeOf(cls, location)
                    : null;
            }

            if (type is null)
            {
                const string message = "The scene's root has neither a type nor a scene it instances; a Node stands in for it.";
                Report(ConversionCodes.RootNotConvertible, ConversionSeverity.Error, message, location);
                type = TypeOf("Node", location);
            }

            XscnNode element = node is null ? new XscnNode { Type = type, Name = "Root" } : NodeElement(node, type);
            var root = new SceneElement(TscnNodePaths.Root, element, null);
            elements[TscnNodePaths.Root] = root;
            return root;
        }

        private void AddNode(TscnNode node)
        {
            string location = NodeLocation(node);
            ReportExtra(node.Extra, location);
            if (node.Parent is not { } parent)
            {
                ReportLostNode("It has no parent= but is not the first node", location);
                return;
            }

            string path = TscnNodePaths.PathOf(parent, node.Name);
            if (IsLost(parent))
            {
                lost.Add(path);
                ReportLostNode($"Its parent '{parent}' was not converted", location);
                return;
            }

            if (elements.TryGetValue(path, out SceneElement? earlier))
            {
                if (earlier.Synthesised && IsOverride(node))
                {
                    Merge(earlier, node);
                    return;
                }

                string message = $"An earlier node at '{parent}' is already named '{node.Name}'; this one is not converted.";
                Report(ConversionCodes.RepeatedSiblingName, ConversionSeverity.Error, message, location);
                return;
            }

            bool converted = IsOverride(node) ? AddOverride(node, parent, path, location) : AddTypedNode(node, parent, path, location);
            if (!converted)
            {
                lost.Add(path);
            }
        }

        private static bool IsOverride(TscnNode node) => node is { Type: null, Instance: null, InstancePlaceholder: null };

        // A section for a node whose override was made earlier, for a node added below it: its properties and fields go
        // into that override, which keeps its children.
        private void Merge(SceneElement earlier, TscnNode node)
        {
            var change = (XscnOverride)earlier.Element;
            earlier.Element = WithNodeFields(change with { Properties = Properties(node.Properties, NodeLocation(node)) }, node);
            earlier.Synthesised = false;
        }

        // A node with no type and no scene changes a node of an instanced scene: an x:Override on the instance.
        private bool AddOverride(TscnNode node, string parent, string path, string location)
        {
            if (Nearest(parent).Scope is not { } scope)
            {
                ReportLostNode($"It has no type and no scene, but its parent '{parent}' is not inside an instanced scene", location);
                return false;
            }

            var change = new XscnOverride { Path = Relative(scope, path), Properties = Properties(node.Properties, location) };
            Add(scope, new SceneElement(path, WithNodeFields(change, node), scope));
            return true;
        }

        private bool AddTypedNode(TscnNode node, string parent, string path, string location)
        {
            XscnType? type =
                node.InstancePlaceholder is not null ? PlaceholderType(node, location)
                : node.Instance is { } instance ? SceneType(instance, location, "the node and its subtree are not converted.")
                : TypeOf(node.Type!, location);
            if (type is null)
            {
                return false;
            }

            if (ElementAt(parent) is not { } container)
            {
                ReportLostNode($"Its parent '{parent}' names no earlier node", location);
                return false;
            }

            Add(container, new SceneElement(path, NodeElement(node, type), null));
            return true;
        }

        // Godot loads a node with both instance= and instance_placeholder= as a placeholder: the placeholder's value is the
        // one kept (resource_format_text.cpp:234-256).
        private XscnType PlaceholderType(TscnNode node, string location)
        {
            if (node.Instance is not null)
            {
                const string message =
                    "This node has both instance= and instance_placeholder=; Godot loads it as a placeholder, so instance= is dropped.";
                Report(ConversionCodes.InstanceAndPlaceholder, ConversionSeverity.Warning, message, location);
            }

            return TypeOf(node.Type ?? PlaceholderClass, location);
        }

        private XscnSceneType? SceneType(GodotValue instance, string location, string consequence)
        {
            if (instance is GodotResourceRef { Kind: GodotRefKind.External } reference && references.Ext(reference.Id) is { } ext)
            {
                return new XscnSceneType(ext.Path);
            }

            string text = GodotValueWriter.Write(instance, ValueWriteOptions.Default);
            string message = $"instance={text} is not ExtResource(\"id\") of an [ext_resource]; {consequence}";
            Report(ConversionCodes.InstanceNotAScene, ConversionSeverity.Error, message, location);
            return null;
        }

        private void AddConnection(TscnConnection connection)
        {
            string location = $"[connection signal=\"{connection.Signal}\" from=\"{connection.From}\"]";
            ReportExtra(connection.Extra, location);
            if (IsLost(connection.From) || ElementAt(connection.From) is not { } emitter)
            {
                string message = $"from=\"{connection.From}\" names no node, inside or outside an instance; the connection is not converted.";
                Report(ConversionCodes.ConnectionEmitterNotFound, ConversionSeverity.Error, message, location);
                return;
            }

            emitter.Connections.Add(
                new XscnConnection(
                    connection.Signal,
                    connection.To,
                    connection.Method,
                    connection.Flags,
                    [.. connection.Binds.Select(bind => ValueReferences.Map(bind, Rewrite))],
                    connection.Unbinds,
                    connection.FromUidPath,
                    connection.ToUidPath
                )
            );
        }

        private void MarkEditable(TscnEditable editable)
        {
            string location = $"[editable path=\"{editable.Path}\"]";
            ReportExtra(editable.Extra, location);
            if (IsLost(editable.Path) || ElementAt(editable.Path) is not { } target)
            {
                string message = $"path=\"{editable.Path}\" names no node; the [editable] is not converted.";
                Report(ConversionCodes.EditableNotFound, ConversionSeverity.Error, message, location);
                return;
            }

            target.Editable = true;
        }

        // The element at a path: a converted node or override, or an override made for a path inside an instance.
        private SceneElement? ElementAt(string path)
        {
            if (elements.TryGetValue(path, out SceneElement? known))
            {
                return known;
            }

            if (Nearest(path).Scope is not { } scope)
            {
                return null;
            }

            var made = new SceneElement(path, new XscnOverride { Path = Relative(scope, path) }, scope) { Synthesised = true };
            Add(scope, made);
            return made;
        }

        // The converted element with the longest path that is the path itself or one of its ancestors; the root at worst.
        private SceneElement Nearest(string path)
        {
            string current = path;
            while (!elements.ContainsKey(current))
            {
                current = TscnNodePaths.ParentOf(current);
            }

            return elements[current];
        }

        private bool IsLost(string path)
        {
            for (string current = path; current != TscnNodePaths.Root; current = TscnNodePaths.ParentOf(current))
            {
                if (lost.Contains(current))
                {
                    return true;
                }
            }

            return false;
        }

        private void Add(SceneElement container, SceneElement element)
        {
            container.Children.Add(element);
            elements[element.Path] = element;
        }

        private static string Relative(SceneElement scope, string path) => scope.Path == TscnNodePaths.Root ? path : path[(scope.Path.Length + 1)..];

        private void ReportLostNode(string reason, string location) =>
            Report(ConversionCodes.NodeParentNotFound, ConversionSeverity.Error, reason + "; it and its subtree are not converted.", location);

        private XscnNode NodeElement(TscnNode node, XscnType type)
        {
            var element = new XscnNode
            {
                Type = type,
                Name = node.Name,
                Unique = node.Properties.Any(IsUniqueName),
                Properties = Properties([.. node.Properties.Where(property => !IsUniqueName(property))], NodeLocation(node)),
            };
            return (XscnNode)WithNodeFields(element, node);
        }

        // unique_name_in_owner = true is the name's %; any other value stays a property.
        private static bool IsUniqueName(TscnProperty property) => property is { Name: UniqueNameProperty, Value: GodotBool { Value: true } };

        private static XscnNodeElement WithNodeFields(XscnNodeElement element, TscnNode node) =>
            element with
            {
                Index = node.Index,
                Owner = node.Owner,
                Groups = node.Groups,
                InstancePlaceholder = node.InstancePlaceholder,
                NodePaths = node.NodePaths,
                ParentIdPath = node.ParentIdPath,
                OwnerUidPath = node.OwnerUidPath,
                UniqueId = node.UniqueId,
            };

        private static string NodeLocation(TscnNode node) =>
            node.Parent is { } parent ? $"[node name=\"{node.Name}\" parent=\"{parent}\"]" : $"[node name=\"{node.Name}\"]";

        // One namespace per res:// folder of the scenes the elements instance, in pre-order; the prefix is the folder's
        // last segment as an XML name, res for res:// itself, and a counter from 2 when an earlier folder took it.
        private static ImmutableArray<XscnNamespace> Namespaces(XscnNodeElement root)
        {
            HashSet<string> taken = new(StringComparer.Ordinal);
            return
            [
                .. SceneFolders(root)
                    .Distinct(StringComparer.Ordinal)
                    .Select(folder => new XscnNamespace(FreePrefix(PrefixOf(folder), taken), folder)),
            ];
        }

        private static IEnumerable<string> SceneFolders(XscnNodeElement element)
        {
            if (element is XscnNode { Type: XscnSceneType scene } && scene.Path.StartsWith("res://", StringComparison.Ordinal))
            {
                yield return scene.Path[..(scene.Path.LastIndexOf('/') + 1)];
            }

            foreach (string folder in element.Children.SelectMany(SceneFolders))
            {
                yield return folder;
            }
        }

        internal static string PrefixOf(string folder)
        {
            if (folder == "res://")
            {
                return "res";
            }

            string segment = folder[..^1];
            segment = segment[(segment.LastIndexOf('/') + 1)..];
            string name = string.Concat(segment.Select(c => XmlConvert.IsNCNameChar(c) ? c : '_'));
            bool usable = name.Length > 0 && XmlConvert.IsStartNCNameChar(name[0]) && !IsReservedPrefix(name);
            return usable ? name : "_" + name;
        }

        // Namespaces in XML 1.0 reserves every name that starts with "xml" in any case, xml and xmlns among them.
        private static bool IsReservedPrefix(string name) => name.StartsWith("xml", StringComparison.OrdinalIgnoreCase);

        internal static string FreePrefix(string prefix, HashSet<string> taken)
        {
            if (taken.Add(prefix))
            {
                return prefix;
            }

            int counter = 2;
            while (!taken.Add(prefix + counter.ToString(CultureInfo.InvariantCulture)))
            {
                counter++;
            }

            return prefix + counter.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A node or override being built: its children and connections are added as the .tscn's sections come.</summary>
        /// <param name="path">The element's path from the root.</param>
        /// <param name="element">The element, without children and connections.</param>
        /// <param name="scope">The instance an override belongs to; null for a node.</param>
        private sealed class SceneElement(string path, XscnNodeElement element, SceneElement? scope)
        {
            public string Path { get; } = path;

            /// <summary>The element, without children and connections; replaced when a later section merges into it.</summary>
            public XscnNodeElement Element { get; set; } = element;

            /// <summary>Whether the element is an override made for a path the .tscn has no section for.</summary>
            public bool Synthesised { get; set; }

            /// <summary>The instance whose nodes a path below this element names: itself when it instances a scene.</summary>
            public SceneElement? Scope => Element is XscnNode { Type: XscnSceneType } ? this : scope;

            public List<SceneElement> Children { get; } = [];

            public List<XscnConnection> Connections { get; } = [];

            public bool Editable { get; set; }

            public XscnNodeElement Build() =>
                Element with
                {
                    Children = [.. Children.Select(child => child.Build())],
                    Connections = [.. Connections],
                    Editable = Editable,
                };
        }
    }

    /// <summary>The prefix a scene folder is named for, shared with the path rewriter.</summary>
    /// <param name="folder">The <c>res://</c> folder.</param>
    /// <returns>The folder's prefix.</returns>
    internal static string FolderPrefix(string folder) => Context.PrefixOf(folder);

    /// <summary>A prefix no namespace of the document has taken yet, shared with the path rewriter.</summary>
    /// <param name="prefix">The prefix wanted.</param>
    /// <param name="taken">The prefixes taken so far, which the returned one is added to.</param>
    /// <returns>The prefix, or the first number after it that is free.</returns>
    internal static string FreeFolderPrefix(string prefix, HashSet<string> taken) => Context.FreePrefix(prefix, taken);
}
