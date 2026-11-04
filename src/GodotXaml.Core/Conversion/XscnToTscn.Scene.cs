using System.Collections.Immutable;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

public static partial class XscnToTscn
{
    private sealed partial class Context
    {
        private const string UniqueNameProperty = "unique_name_in_owner";
        private const string LayoutModeProperty = "layout_mode";

        // Control::_set_layout_mode's LAYOUT_MODE_CONTAINER, the mode a Container gives every Control child it lays out.
        private const int ContainerLayoutMode = 2;

        // An .xscn becomes a .tscn: the element tree flattened in pre-order (packed_scene.cpp:1129-1131), the files found
        // per node, its instanced scene before its properties' (packed_scene.cpp:844-853, 1000), and the connections'
        // binds after every node (packed_scene.cpp:1325, 1372); the header takes the uid the host passes.
        public TscnDocument ConvertScene()
        {
            if (document.Root is not XscnNode root)
            {
                string message = "The root of a scene document is a resource; a scene's root is a node, so no node is written.";
                Report(ConversionCodes.SceneRootNotANode, ConversionSeverity.Error, message, RootLocation);
                return new TscnDocument
                {
                    Kind = TscnKind.Scene,
                    Header = new TscnHeader { Uid = options.Uid },
                };
            }

            List<PlacedElement> placed = [];
            Place(root, null, placed);
            placed.ForEach(DiscoverElement);
            placed.ForEach(DiscoverBinds);
            ReportUnusedEntries();
            TscnDocument converted = MintIds(new TscnDocument { Kind = TscnKind.Scene }) with
            {
                Nodes = [.. placed.Where(WritesNode).Select(SceneNode)],
                Connections = [.. placed.SelectMany(SceneConnections)],
                Editables = [.. placed.Where(element => element.Element.Editable).Select(element => new TscnEditable { Path = element.Path })],
            };
            return converted with
            {
                Header = new TscnHeader { Uid = options.Uid, Format = TscnWriter.FormatOf(converted) },
            };
        }

        // An override's path is relative to the instance it is a direct child of (DECISIONS "Overrides inside an
        // instance are named by path"); elsewhere it is reported and taken under its parent all the same.
        private void Place(XscnNodeElement element, PlacedElement? parent, List<PlacedElement> placed)
        {
            PlacedElement? here = element switch
            {
                XscnNode node => new PlacedElement(element, node.Name, TscnNodePaths.PathOf(parent?.Path, node.Name), parent?.Path)
                {
                    ParentElement = parent,
                },
                XscnOverride over => PlaceOverride(over, parent!),
                _ => throw new InvalidOperationException($"No conversion for a {element.GetType().Name}."),
            };
            if (here is null)
            {
                return;
            }

            placed.Add(here);
            foreach (XscnNodeElement child in element.Children)
            {
                Place(child, here, placed);
            }
        }

        private PlacedElement? PlaceOverride(XscnOverride over, PlacedElement parent)
        {
            if (over.Path.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                string message =
                    $"This x:Override has no Path, or a Path with an empty, . or .. segment (\"{over.Path}\"), so it names no node; "
                    + "it and what it holds are not written.";
                Report(ConversionCodes.OverrideWithoutPath, ConversionSeverity.Error, message, Location(parent.Path));
                return null;
            }

            string path = parent.Path == TscnNodePaths.Root ? over.Path : parent.Path + "/" + over.Path;
            string? instanced = InstancedScene(parent.Element);
            if (instanced is null)
            {
                string message = $"x:Override Path=\"{over.Path}\" is not a direct child of an instance; its path is taken under its parent.";
                Report(ConversionCodes.OverrideOutsideInstance, ConversionSeverity.Error, message, Location(path));
            }

            int slash = path.LastIndexOf('/');
            return new PlacedElement(over, path[(slash + 1)..], path, TscnNodePaths.ParentOf(path))
            {
                InstancedBy = instanced,
                ParentElement = parent,
            };
        }

        private void DiscoverElement(PlacedElement placed)
        {
            string location = Location(placed.Path);
            if (InstancedScene(placed.Element) is { } scene)
            {
                DiscoverFile(scene, "PackedScene", null, location);
            }

            // The node's files in the order its section writes its properties (packed_scene.cpp:860-866): the same class and
            // sort NodeProperties uses, so the two agree.
            ImmutableArray<XscnProperty> sorted = SortedProperties(placed.Element);
            GodotClassInfo? cls = NodeClass(placed, sorted, location);
            ImmutableArray<XscnProperty> ordered = order.Sort(cls, RestoreLayoutMode(placed, sorted), sources);
            ReportSourceFailures(location);
            DiscoverInOrder((placed.Element as XscnNode)?.Type, ordered, location, placed.Path);
        }

        private void DiscoverBinds(PlacedElement placed)
        {
            foreach (XscnConnection connection in placed.Element.Connections)
            {
                string site = $"{placed.Path}|signal {connection.Signal}|{connection.To}|{connection.Method}";
                for (int i = 0; i < connection.Binds.Length; i++)
                {
                    DiscoverValue(
                        new XscnScalar(connection.Binds[i]),
                        null,
                        $"{Location(placed.Path)} signal {connection.Signal}",
                        $"{site}|bind[{i}]"
                    );
                }
            }
        }

        // A placeholder holds its scene's path, not the scene (packed_scene.cpp:842-845), so it has no ext_resource.
        private static string? InstancedScene(XscnNodeElement element) =>
            element is XscnNode { Type: XscnSceneType scene, InstancePlaceholder: null } ? scene.Path : null;

        // An override that changes nothing of its node is only the way to its children and connections.
        private static bool WritesNode(PlacedElement placed) =>
            placed.Element is XscnNode || !placed.Element.Properties.IsEmpty || !placed.Element.Groups.IsEmpty || HasHeaderFields(placed.Element);

        private static bool HasHeaderFields(XscnNodeElement element) =>
            element.Index is not null
            || element.Owner is not null
            || element.UniqueId is not null
            || element.InstancePlaceholder is not null
            || !element.NodePaths.IsEmpty
            || !element.ParentIdPath.IsEmpty
            || !element.OwnerUidPath.IsEmpty;

        private TscnNode SceneNode(PlacedElement placed)
        {
            XscnNodeElement element = placed.Element;
            return new TscnNode
            {
                Name = placed.Name,
                Type = NodeTypeName(element, Location(placed.Path)),
                Parent = placed.Parent,
                Owner = element.Owner,
                Index = element.Index,
                UniqueId = element.UniqueId,
                NodePaths = element.NodePaths,
                Groups = element.Groups,
                InstancePlaceholder = element.InstancePlaceholder,
                Instance = InstancedScene(element) is { } scene ? new GodotResourceRef(GodotRefKind.External, extIds[scene]) : null,
                ParentIdPath = element.ParentIdPath,
                OwnerUidPath = element.OwnerUidPath,
                Properties = NodeProperties(placed, Location(placed.Path)),
            };
        }

        // Name="%X" is unique_name_in_owner = true, written first; an explicit one says the same, or loses to it.
        private ImmutableArray<TscnProperty> NodeProperties(PlacedElement placed, string location)
        {
            XscnNodeElement element = placed.Element;
            ImmutableArray<XscnProperty> sorted = SortedProperties(element);
            GodotClassInfo? cls = NodeClass(placed, sorted, location);
            sorted = RestoreLayoutMode(placed, sorted);
            if (element is not XscnNode { Unique: true } node)
            {
                return Properties(cls, sorted, location);
            }

            ImmutableArray<XscnProperty> explicitOnes = [.. node.Properties.Where(property => property.GodotName == UniqueNameProperty)];
            if (explicitOnes.Any(property => property.Value is not XscnScalar { Value: GodotBool { Value: true } }))
            {
                string message = $"Name=\"%{node.Name}\" makes the node unique, but unique_name_in_owner is false; the node is written unique.";
                Report(ConversionCodes.UniqueNameConflict, ConversionSeverity.Warning, message, location);
            }

            return Properties(cls, sorted, location).Insert(0, new TscnProperty(UniqueNameProperty, new GodotBool(true)));
        }

        // The properties a node's section sorts: for Name="%X", all but an explicit unique_name_in_owner, which goes first.
        private static ImmutableArray<XscnProperty> SortedProperties(XscnNodeElement element) =>
            element is XscnNode { Unique: true }
                ? [.. element.Properties.Where(property => property.GodotName != UniqueNameProperty)]
                : element.Properties;

        // The class that sets a node's property order: its tag's, or for an instance, an inherited root or an override the
        // class of its node in the instanced scene, which only the host can read; one property needs no order. A
        // placeholder replays its values in file order on its scene's root (instance_placeholder.cpp:37-43, 100-102), so
        // the root's class orders them as an instance's. A tag the metadata does not know keeps source order.
        private GodotClassInfo? NodeClass(PlacedElement placed, ImmutableArray<XscnProperty> properties, string location) =>
            placed.Element switch
            {
                XscnNode { InstancePlaceholder: { } placeholder } => ClassInScene(placeholder, TscnNodePaths.Root, properties.Length, location),
                XscnNode { Type: XscnClassType tagged } => tagged.Class,
                XscnNode { Type: XscnSceneType scene } => ClassInScene(scene.Path, TscnNodePaths.Root, properties.Length, location),
                XscnOverride over when placed.InstancedBy is { } scene => ClassInScene(scene, over.Path, properties.Length, location),
                _ => null,
            };

        // The .xscn omits layout_mode under a Container, since the parent decides it (DECISIONS "a value the parent decides");
        // the way back writes the value the parent decides, so the section holds what Godot wrote. Only a node whose tag
        // names its class, under a parent whose tag names a Container, gets it: an instance or an override takes its class
        // from another file, and a node with its own layout_mode keeps it. The class test is the writer's, so the two agree.
        private ImmutableArray<XscnProperty> RestoreLayoutMode(PlacedElement placed, ImmutableArray<XscnProperty> properties)
        {
            if (
                TagClass(placed.Element) is not { } cls
                || !metadata.Inherits(cls, "Control")
                || metadata.FindProperty(cls, LayoutModeProperty) is null
                || properties.Any(property => property.GodotName == LayoutModeProperty)
                || placed.ParentElement is not { } parent
                || TagClass(parent.Element) is not { } parentClass
                || !metadata.Inherits(parentClass, "Container")
            )
            {
                return properties;
            }

            return [.. properties, new XscnProperty(LayoutModeProperty, new XscnScalar(new GodotInt(ContainerLayoutMode)))];
        }

        // The class an element's tag names; null for an instance, a placeholder or an override, whose class only another
        // file knows, and for a tag the metadata lacks.
        private static GodotClassInfo? TagClass(XscnNodeElement element) =>
            element is XscnNode { Type: XscnClassType tagged, InstancePlaceholder: null } ? tagged.Class : null;

        // A scene that cannot be read is warned about once per reference and reason, at the first section that meets it.
        // The scene's uid is the one its ext_resource is written with.
        private GodotClassInfo? ClassInScene(string scene, string path, int count, string location)
        {
            if (count < 2 || instancedClasses is null)
            {
                return null;
            }

            InstancedClass found = instancedClasses.ClassOf(scene, UidOf(scene), path);
            if (found.Failure is { } failure && unreadableScenes.Add((failure.Reference, failure.Reason)))
            {
                string message =
                    $"The class of \"{failure.Reference}\" could not be read ({failure.Reason}), so this section keeps its written property order.";
                Report(ConversionCodes.InstancedClassUnreadable, ConversionSeverity.Warning, message, location);
            }

            return found.Class;
        }

        // An instanced, inherited or placeholder node is made by its scene, so the .tscn names no class for it
        // (packed_scene.cpp:1052-1063); neither does an override. A placeholder tagged otherwise than InstancePlaceholder
        // loses its tag.
        private string? NodeTypeName(XscnNodeElement element, string location)
        {
            if (element is XscnNode { InstancePlaceholder: { } placeholder } node)
            {
                if (node.Type is not XscnClassType { Class.Name: "InstancePlaceholder" })
                {
                    string message =
                        $"x:InstancePlaceholder makes this node a placeholder for {placeholder}; its tag {TagOf(node.Type)} has no place in "
                        + "the .tscn and is dropped.";
                    Report(ConversionCodes.PlaceholderTypeDropped, ConversionSeverity.Warning, message, location);
                }

                return null;
            }

            return element switch
            {
                XscnNode { Type: XscnClassType classType } => classType.Class.Name,
                XscnNode { Type: XscnUnknownType unknown } => unknown.Name,
                _ => null,
            };
        }

        private static string TagOf(XscnType type) =>
            type switch
            {
                XscnClassType classType => classType.Class.Name,
                XscnUnknownType unknown => unknown.Name,
                XscnSceneType scene => $"(the scene {scene.Path})",
                _ => type.GetType().Name,
            };

        private IEnumerable<TscnConnection> SceneConnections(PlacedElement placed) =>
            placed.Element.Connections.Select(connection => new TscnConnection
            {
                Signal = connection.Signal,
                From = placed.Path,
                To = connection.To,
                Method = connection.Method,
                Flags = connection.Flags,
                Unbinds = connection.Unbinds,
                Binds = [.. connection.Binds.Select(bind => ValueReferences.Map(bind, Rewrite))],
                FromUidPath = connection.FromUidPath,
                ToUidPath = connection.ToUidPath,
            });

        private static string Location(string path) => $"node \"{path}\"";

        // An element with its name and its path as the .tscn writes them, and its parent's path (null for the root).
        private sealed record PlacedElement(XscnNodeElement Element, string Name, string Path, string? Parent)
        {
            // For an override, the scene its parent instances, where its path leads; null for a node.
            public string? InstancedBy { get; init; }

            // The element this one was placed under, whose class decides a Control child's layout_mode.
            public PlacedElement? ParentElement { get; init; }
        }
    }
}
