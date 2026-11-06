using System.Collections.Immutable;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

public static partial class TscnReader
{
    // The section tags the loader knows (resource_format_text.cpp:191-418, 460-905), each read into the builder; the
    // function returns what to do with the section's properties once they are read.
    private static readonly Dictionary<string, Func<DocumentBuilder, TscnTagFields, Action<ImmutableArray<TscnProperty>>>> SectionReaders = new(
        StringComparer.Ordinal
    )
    {
        ["ext_resource"] = (builder, fields) => builder.ReadExtResource(fields),
        ["sub_resource"] = (builder, fields) => builder.ReadSubResource(fields),
        ["resource"] = (builder, _) => builder.ReadResource(),
        ["node"] = (builder, fields) => builder.ReadNode(fields),
        ["connection"] = (builder, fields) => builder.ReadConnection(fields),
        ["editable"] = (builder, fields) => builder.ReadEditable(fields),
    };

    // The known tags only one kind of file may hold outside a nested scene (4.8 resource_format_text.cpp:861-867,
    // 894-898); a scene's [resource] holds the PackedScene's own properties (4.8 :697-836).
    private static readonly Dictionary<string, TscnKind> SectionKinds = new(StringComparer.Ordinal)
    {
        ["node"] = TscnKind.Scene,
        ["connection"] = TscnKind.Scene,
        ["editable"] = TscnKind.Scene,
    };

    // The order the loader reads the sections in (resource_format_text.cpp:462-538, 547-717, 719-847, 868-908): ext
    // resources, then sub resources, then one [resource] or the scene's tags; a section whose rank is below the
    // highest one read so far comes after the tag that ends its loop, and the loader calls it unknown.
    private static readonly Dictionary<string, int> SectionRanks = new(StringComparer.Ordinal)
    {
        ["ext_resource"] = 0,
        ["sub_resource"] = 1,
        ["resource"] = 2,
        ["node"] = 2,
        ["connection"] = 2,
        ["editable"] = 2,
    };

    private sealed class DocumentBuilder((TscnKind Kind, TscnHeader Header) start)
    {
        private readonly TscnKind _kind = start.Kind;
        private readonly HashSet<string> _extIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _subIds = new(StringComparer.Ordinal);
        private readonly List<TscnExtResource> _extResources = [];
        private readonly List<TscnSubResource> _subResources = [];
        private readonly List<TscnProperty> _resourceProperties = [];
        private readonly List<TscnProperty> _sceneProperties = [];
        private readonly SceneLists _main = new();
        private SceneLists? _nested;
        private List<TscnProperty>? _properties;
        private Action<ImmutableArray<TscnProperty>>? _finish;
        private TscnTag? _tag;
        private TscnTag? _firstAfterResources;
        private int _rank = -1;
        private bool _resourceSeen;

        public void Add(TscnItem item)
        {
            if (item is TscnAssign assign)
            {
                AddProperty(assign);
                return;
            }

            Finish();
            Start((TscnTag)item);
        }

        // The scene the node, connection and editable tags read now go to: a PackedScene sub-resource's own, or the main one.
        private SceneLists Scene => _nested ?? _main;

        public TscnDocument Build(TscnScanner scanner)
        {
            Finish();
            CloseNestedAtEnd();
            CheckEnd(scanner);
            return new TscnDocument
            {
                Kind = _kind,
                Header = start.Header,
                ExtResources = [.. _extResources],
                SubResources = [.. _subResources],
                ResourceProperties = [.. _resourceProperties],
                SceneProperties = [.. _sceneProperties],
                HasSceneResourceSection = _kind == TscnKind.Scene && _resourceSeen,
                Nodes = [.. _main.Nodes],
                Connections = [.. _main.Connections],
                Editables = [.. _main.Editables],
            };
        }

        private void CheckEnd(TscnScanner scanner)
        {
            if (_kind == TscnKind.Scene && _tag?.Name == "resource")
            {
                // 4.8 resource_format_text.cpp:758-764: the file ending inside a scene's [resource] is ERR_FILE_CORRUPT.
                throw TscnScanner.Error("Scene files need to have at least one node. None was found.", scanner.Line, scanner.Column);
            }

            if (_kind == TscnKind.Resource && !_resourceSeen)
            {
                // resource_format_text.cpp:1193-1200: the tag read after the header is a parse error whose text load()
                // reports as "Unexpected end of file", whatever the file held before it.
                throw TscnScanner.Error("Unexpected end of file", scanner.Line, scanner.Column);
            }

            if (_kind == TscnKind.Scene && _main.Nodes.Count == 0)
            {
                // resource_format_text.cpp:532-537, 646-651, 1194-1200: the resource loops and the tag read after the
                // header end the file with the parser's ERR_FILE_EOF, which load() reports with no message; GXAML-40
                // measured it and names it "Unexpected end of file".
                throw TscnScanner.Error("Unexpected end of file", scanner.Line, scanner.Column);
            }
        }

        public Action<ImmutableArray<TscnProperty>> ReadExtResource(TscnTagFields fields)
        {
            // resource_format_text.cpp:466-489: path, then type, then id.
            string path = fields.Required("path", "Missing 'path' in external resource tag");
            string type = fields.Required("type", "Missing 'type' in external resource tag");
            string id = fields.Required("id", "Missing 'id' in external resource tag");
            _extIds.Add(id);
            _extResources.Add(
                new TscnExtResource
                {
                    Type = type,
                    Uid = fields.String("uid"),
                    Path = path,
                    Id = id,
                    Extra = fields.Rest(),
                }
            );
            return NoProperties;
        }

        public Action<ImmutableArray<TscnProperty>> ReadSubResource(TscnTagFields fields)
        {
            // resource_format_text.cpp:552-564; Godot's text says "external" here too.
            string type = fields.Required("type", "Missing 'type' in external resource tag");
            string id = fields.Required("id", "Missing 'id' in external resource tag");
            _subIds.Add(id);
            ImmutableArray<TscnField> extra = fields.Rest();
            return properties =>
                _subResources.Add(
                    new TscnSubResource
                    {
                        Type = type,
                        Id = id,
                        Properties = properties,
                        Extra = extra,
                    }
                );
        }

        // 4.8 resource_format_text.cpp:822-828: a scene's [resource] sets the PackedScene's own properties, and a second
        // one adds to them, as the loop re-enters on another resource tag.
        public Action<ImmutableArray<TscnProperty>> ReadResource() =>
            _kind == TscnKind.Scene ? properties => _sceneProperties.AddRange(properties) : properties => _resourceProperties.AddRange(properties);

        public Action<ImmutableArray<TscnProperty>> ReadNode(TscnTagFields fields)
        {
            // resource_format_text.cpp:243-253: a placeholder on the node before any other of its scene is
            // ERR_FILE_CORRUPT, since that node has nothing to inherit from.
            List<TscnNode> nodes = Scene.Nodes;
            if (fields.Value("instance_placeholder") is not null && nodes.Count == 0)
            {
                throw Missing("Instance Placeholder can't be used for inheritance");
            }

            // resource_format_text.cpp:202-204: a node without a name field keeps the unassigned name the loader gives it.
            var node = new TscnNode
            {
                Name = fields.Name("name") ?? "",
                Type = fields.Name("type"),
                Parent = fields.Path("parent"),
                Owner = fields.Path("owner"),
                Index = fields.ToInt("index"),
                UniqueId = fields.Int("unique_id"),
                NodePaths = Strings(fields, "node_paths"),
                Groups = Strings(fields, "groups"),
                // :244 reads the placeholder through Variant::operator String, not a name or a path.
                InstancePlaceholder = fields.String("instance_placeholder"),
                Instance = fields.Value("instance"),
                ParentIdPath = fields.Ints("parent_id_path"),
                OwnerUidPath = fields.Ints("owner_uid_path"),
            };
            ImmutableArray<TscnField> extra = fields.Rest();
            return properties => nodes.Add(node with { Properties = properties, Extra = extra });
        }

        public Action<ImmutableArray<TscnProperty>> ReadConnection(TscnTagFields fields)
        {
            // resource_format_text.cpp:312-335.
            var connection = new TscnConnection
            {
                From = fields.Path("from") ?? throw Missing("missing 'from' field from connection tag"),
                To = fields.Path("to") ?? throw Missing("missing 'to' field from connection tag"),
                Signal = fields.Name("signal") ?? throw Missing("missing 'signal' field from connection tag"),
                Method = fields.Name("method") ?? throw Missing("missing 'method' field from connection tag"),
                FromUidPath = fields.Ints("from_uid_path"),
                ToUidPath = fields.Ints("to_uid_path"),
                Binds = Binds(fields),
            };
            Scene.Connections.Add(
                connection with
                {
                    Flags = fields.Int("flags") ?? TscnConnection.PersistFlags,
                    Unbinds = fields.Int("unbinds") ?? 0,
                    Extra = fields.Rest(),
                }
            );
            return NoProperties;
        }

        public Action<ImmutableArray<TscnProperty>> ReadEditable(TscnTagFields fields)
        {
            string path = fields.Path("path") ?? throw Missing("Missing 'path' field from editable tag");
            Scene.Editables.Add(new TscnEditable { Path = path, Extra = fields.Rest() });
            return NoProperties;
        }

        private void Start(TscnTag tag)
        {
            CheckSection(tag);
            foreach (TscnTagField field in tag.Fields)
            {
                CheckReferences(field.Value, field.Line, field.Column);
            }

            var fields = new TscnTagFields(tag);
            _tag = tag;
            _properties = [];
            _finish = SectionReaders[tag.Name](this, fields);
        }

        // resource_format_text.cpp:462-538, 547-717, 719-847, 868-908: the loader reads the sections in its own order
        // and names an unknown or misplaced tag with its own text.
        private void CheckSection(TscnTag tag)
        {
            if (_kind == TscnKind.Resource && _resourceSeen)
            {
                // :839-843: the main resource ends the file.
                throw TscnScanner.Error("Extra tag found when parsing main resource file", tag.Line, tag.Column);
            }

            if (!GoesToNestedScene(tag))
            {
                CheckMainSection(tag);
            }
        }

        // 4.8 :186-408, 674-681: whether the tag belongs to a PackedScene sub-resource's own scene, opening one on its
        // first [node]. A nested scene reads node, connection and editable tags until a sub_resource or a resource tag
        // returns it to the resource loops; any other tag is unknown.
        private bool GoesToNestedScene(TscnTag tag)
        {
            if (_nested is null)
            {
                if (!StartsNestedScene(tag))
                {
                    return false;
                }

                _nested = new SceneLists();
                return true;
            }

            if (tag.Name is "node" or "connection" or "editable")
            {
                return true;
            }

            if (tag.Name is not ("sub_resource" or "resource"))
            {
                throw Unknown(tag);
            }

            CloseNested();
            return false;
        }

        private void CheckMainSection(TscnTag tag)
        {
            // 4.8 :402-408: the main scene is not nested, so a [resource] after its first node is unknown too.
            if (!SectionRanks.TryGetValue(tag.Name, out int rank) || rank < _rank || ResourceAfterTheMainScene(tag))
            {
                throw Unknown(tag);
            }

            if (SectionKinds.TryGetValue(tag.Name, out TscnKind only) && only != _kind)
            {
                throw WrongKind(tag);
            }

            if (FirstAfterResourcesIsNotANode(tag))
            {
                throw Unknown(tag);
            }

            NoteFirstAfterResources(tag);
            _rank = rank;
            _resourceSeen |= tag.Name == "resource";
        }

        private bool ResourceAfterTheMainScene(TscnTag tag) => _firstAfterResources is not null && tag.Name == "resource";

        private void NoteFirstAfterResources(TscnTag tag)
        {
            if (_kind == TscnKind.Scene && _firstAfterResources is null && tag.Name is not ("ext_resource" or "sub_resource" or "resource"))
            {
                _firstAfterResources = tag;
            }
        }

        // 4.8 :674-681: a [node] right after a PackedScene sub-resource's properties starts that sub-resource's own
        // scene. 4.8's loader does not look at _bundled, so it would read a 4.7 file whose last sub-resource is a
        // PackedScene with its main nodes inside that sub-resource; the reader starts a nested scene only when the
        // sub-resource has no _bundled (every 4.7-saved PackedScene has one), so such a file keeps its main nodes.
        private bool StartsNestedScene(TscnTag tag) =>
            tag.Name == "node"
            && _tag?.Name == "sub_resource"
            && _subResources[^1] is { Type: "PackedScene" } sub
            && !sub.Properties.Any(property => property.Name == "_bundled");

        // Attaches the nested scene read so far to its sub-resource.
        private void CloseNested()
        {
            if (_nested is null)
            {
                return;
            }

            // No sub-resource is read while a nested scene is open, so its owner is the last one.
            _subResources[^1] = _subResources[^1] with
            {
                Scene = _nested.ToScene(),
            };
            _nested = null;
        }

        // Godot 4.7.2 skips a property equal to its default (resource_format_text.cpp:1986-1988), so an unpacked
        // PackedScene it saved has no _bundled, and the main scene's nodes follow it; 4.8 always ends a nested scene
        // with [sub_resource] or [resource] (4.8 :2122-2134). A nested scene still open at the end of a scene with no
        // main node is therefore the main scene.
        private void CloseNestedAtEnd()
        {
            if (_kind == TscnKind.Scene && _nested is not null && _main.Nodes.Count == 0)
            {
                _main.AddFrom(_nested);
                _nested = null;
            }

            CloseNested();
        }

        // :904.
        private static TscnParseException Unknown(TscnTag tag) => TscnScanner.Error($"Unknown tag '{tag.Name}' in file", tag.Line, tag.Column);

        // 4.8 :859-899: a scene's first tag after its resources and its [resource] enters _parse_node_tag only when it
        // is [node]; a connection or an editable there is named as unknown.
        private bool FirstAfterResourcesIsNotANode(TscnTag tag) =>
            _kind == TscnKind.Scene && _firstAfterResources is null && tag.Name is not ("ext_resource" or "sub_resource" or "resource" or "node");

        // 4.8 :861-867 names a [node] in a resource file; :894-898 leaves the other scene-only tags unknown.
        private static TscnParseException WrongKind(TscnTag tag) =>
            tag.Name == "node" ? TscnScanner.Error("Unexpected 'node' tag in a resource file", tag.Line, tag.Column) : Unknown(tag);

        private void AddProperty(TscnAssign assign)
        {
            if (_properties is null)
            {
                throw TscnScanner.Error("Expected '['", assign.Line, assign.Column);
            }

            CheckReferences(assign.Value, assign.Line, assign.Column);
            _properties.Add(new TscnProperty(assign.Name, assign.Value));
        }

        private void Finish()
        {
            _finish?.Invoke([.. _properties!]);
            _finish = null;
        }

        private TscnParseException Missing(string reason) => TscnScanner.Error(reason, _tag!.Line, _tag.Column);

        private void NoProperties(ImmutableArray<TscnProperty> properties)
        {
            if (!properties.IsEmpty)
            {
                throw TscnScanner.Error($"Unexpected property '{properties[0].Name}' in '{_tag!.Name}' tag", _tag.Line, _tag.Column);
            }
        }

        // resource_format_text.cpp:105-141: an id no earlier section defined is a load error.
        private void CheckReferences(GodotValue value, int line, int column)
        {
            foreach (GodotResourceRef reference in TscnValues.SelfAndDescendants(value).OfType<GodotResourceRef>())
            {
                bool external = reference.Kind == GodotRefKind.External;
                if (!(external ? _extIds : _subIds).Contains(reference.Id))
                {
                    string kind = external ? "ext-resource" : "sub-resource";
                    throw TscnScanner.Error($"Can't load cached {kind} id: {reference.Id}", line, column);
                }
            }
        }
    }

    // The node, connection and editable sections of one scene, the main one or a PackedScene sub-resource's own.
    private sealed class SceneLists
    {
        public List<TscnNode> Nodes { get; } = [];

        public List<TscnConnection> Connections { get; } = [];

        public List<TscnEditable> Editables { get; } = [];

        public void AddFrom(SceneLists other)
        {
            Nodes.AddRange(other.Nodes);
            Connections.AddRange(other.Connections);
            Editables.AddRange(other.Editables);
        }

        public TscnNestedScene ToScene() =>
            new()
            {
                Nodes = [.. Nodes],
                Connections = [.. Connections],
                Editables = [.. Editables],
            };
    }
}
