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

    // The known tags only one kind of file may hold (resource_format_text.cpp:723-727, 870-874).
    private static readonly Dictionary<string, TscnKind> SectionKinds = new(StringComparer.Ordinal)
    {
        ["resource"] = TscnKind.Resource,
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
        private readonly List<TscnNode> _nodes = [];
        private readonly List<TscnConnection> _connections = [];
        private readonly List<TscnEditable> _editables = [];
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

        public TscnDocument Build(TscnScanner scanner)
        {
            Finish();
            if (_kind == TscnKind.Resource && !_resourceSeen)
            {
                // resource_format_text.cpp:1193-1200: the tag read after the header is a parse error whose text load()
                // reports as "Unexpected end of file", whatever the file held before it.
                throw TscnScanner.Error("Unexpected end of file", scanner.Line, scanner.Column);
            }

            if (_kind == TscnKind.Scene && _nodes.Count == 0)
            {
                // resource_format_text.cpp:532-537, 646-651, 1194-1200: the resource loops and the tag read after the
                // header end the file with the parser's ERR_FILE_EOF, which load() reports with no message; GXAML-40
                // measured it and names it "Unexpected end of file".
                throw TscnScanner.Error("Unexpected end of file", scanner.Line, scanner.Column);
            }

            return new TscnDocument
            {
                Kind = _kind,
                Header = start.Header,
                ExtResources = [.. _extResources],
                SubResources = [.. _subResources],
                ResourceProperties = [.. _resourceProperties],
                Nodes = [.. _nodes],
                Connections = [.. _connections],
                Editables = [.. _editables],
            };
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

        public Action<ImmutableArray<TscnProperty>> ReadResource() => properties => _resourceProperties.AddRange(properties);

        public Action<ImmutableArray<TscnProperty>> ReadNode(TscnTagFields fields)
        {
            // resource_format_text.cpp:243-253: a placeholder on the node before any other is ERR_FILE_CORRUPT, since
            // that node has nothing to inherit from.
            if (fields.Value("instance_placeholder") is not null && _nodes.Count == 0)
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
            return properties => _nodes.Add(node with { Properties = properties, Extra = extra });
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
            _connections.Add(
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
            _editables.Add(new TscnEditable { Path = path, Extra = fields.Rest() });
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
            if (_firstAfterResources is null && tag.Name is not ("ext_resource" or "sub_resource"))
            {
                _firstAfterResources = tag;
            }

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

            if (!SectionRanks.TryGetValue(tag.Name, out int rank) || rank < _rank)
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

            _rank = rank;
            _resourceSeen |= tag.Name == "resource";
        }

        // :904.
        private static TscnParseException Unknown(TscnTag tag) => TscnScanner.Error($"Unknown tag '{tag.Name}' in file", tag.Line, tag.Column);

        // :868-908: a scene's first tag after its resources enters _parse_node_tag only when it is [node]; a connection
        // or an editable there is named as unknown.
        private bool FirstAfterResourcesIsNotANode(TscnTag tag) =>
            _kind == TscnKind.Scene && _firstAfterResources is null && tag.Name is not ("ext_resource" or "sub_resource" or "node");

        // :725, 872 name a [resource] in a scene and a [node] in a resource; :904 leaves the other scene-only tags unknown.
        private TscnParseException WrongKind(TscnTag tag)
        {
            string file = _kind == TscnKind.Scene ? "scene" : "resource";
            return tag.Name is "resource" or "node"
                ? TscnScanner.Error($"Unexpected '{tag.Name}' tag in a {file} file", tag.Line, tag.Column)
                : Unknown(tag);
        }

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
}
