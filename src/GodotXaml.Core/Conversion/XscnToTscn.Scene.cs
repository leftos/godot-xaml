using System.Collections.Immutable;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

public static partial class XscnToTscn
{
    private sealed partial class Context
    {
        private const string UniqueNameProperty = "unique_name_in_owner";

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
                XscnNode node => new PlacedElement(element, node.Name, TscnNodePaths.PathOf(parent?.Path, node.Name), parent?.Path),
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
            if (InstancedScene(parent.Element) is null)
            {
                string message = $"x:Override Path=\"{over.Path}\" is not a direct child of an instance; its path is taken under its parent.";
                Report(ConversionCodes.OverrideOutsideInstance, ConversionSeverity.Error, message, Location(path));
            }

            int slash = path.LastIndexOf('/');
            return new PlacedElement(over, path[(slash + 1)..], path, TscnNodePaths.ParentOf(path));
        }

        private void DiscoverElement(PlacedElement placed)
        {
            string location = Location(placed.Path);
            if (InstancedScene(placed.Element) is { } scene)
            {
                DiscoverFile(scene, "PackedScene", null, location);
            }

            DiscoverProperties((placed.Element as XscnNode)?.Type, placed.Element.Properties, location, placed.Path);
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
                Properties = NodeProperties(element, Location(placed.Path)),
            };
        }

        // Name="%X" is unique_name_in_owner = true, written first; an explicit one says the same, or loses to it.
        private ImmutableArray<TscnProperty> NodeProperties(XscnNodeElement element, string location)
        {
            if (element is not XscnNode { Unique: true } node)
            {
                return Properties(element.Properties, location);
            }

            ImmutableArray<XscnProperty> explicitOnes = [.. node.Properties.Where(property => property.GodotName == UniqueNameProperty)];
            if (explicitOnes.Any(property => property.Value is not XscnScalar { Value: GodotBool { Value: true } }))
            {
                string message = $"Name=\"%{node.Name}\" makes the node unique, but unique_name_in_owner is false; the node is written unique.";
                Report(ConversionCodes.UniqueNameConflict, ConversionSeverity.Warning, message, location);
            }

            ImmutableArray<XscnProperty> rest = [.. node.Properties.Where(property => property.GodotName != UniqueNameProperty)];
            return Properties(rest, location).Insert(0, new TscnProperty(UniqueNameProperty, new GodotBool(true)));
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
        private sealed record PlacedElement(XscnNodeElement Element, string Name, string Path, string? Parent);
    }
}
