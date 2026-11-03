using System.Collections.Frozen;
using System.Collections.Immutable;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Converts an .xscn or .xres model to a .tscn or .tres model, making the sections and ids again as Godot 4.7.2's text
/// saver orders them (resource_format_text.cpp:1652-1748): ext_resources numbered in first-use order and written in
/// natural id order, sub_resources after the resources they use, a resource's references followed in the sorted order of
/// its property names (resource_format_text.cpp:1680-1683) and a node's in the list order its section writes them in
/// (packed_scene.cpp:860-866). A scene's nodes are flattened in tree order, each node's instanced scene found before its
/// properties' files and the connections' binds after every node's (packed_scene.cpp:844-853, 1000, 1325), then come the
/// connections and the editable instances in the same order. What the .tscn cannot hold is reported as an issue, never
/// thrown.
/// </summary>
public static partial class XscnToTscn
{
    // The class an ext_resource's file most likely holds, by extension, when the host does not say.
    private static readonly FrozenDictionary<string, string> TypesByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".tscn"] = "PackedScene",
        [".xscn"] = "PackedScene",
        [".scn"] = "PackedScene",
        [".tres"] = "Resource",
        [".xres"] = "Resource",
        [".res"] = "Resource",
        [".json"] = "JSON",
        [".po"] = "Translation",
        [".mo"] = "Translation",
        [".gdextension"] = "GDExtension",
        [".gd"] = "Script",
        [".cs"] = "Script",
        [".gdshader"] = "Shader",
        [".gdshaderinc"] = "ShaderInclude",
        [".png"] = "Texture2D",
        [".jpg"] = "Texture2D",
        [".jpeg"] = "Texture2D",
        [".webp"] = "Texture2D",
        [".svg"] = "Texture2D",
        [".bmp"] = "Texture2D",
        [".tga"] = "Texture2D",
        [".exr"] = "Texture2D",
        [".hdr"] = "Texture2D",
        [".dds"] = "Texture2D",
        [".ktx"] = "Texture2D",
        [".ttf"] = "FontFile",
        [".otf"] = "FontFile",
        [".woff"] = "FontFile",
        [".woff2"] = "FontFile",
        [".wav"] = "AudioStream",
        [".ogg"] = "AudioStream",
        [".ogv"] = "VideoStreamTheora",
        [".mp3"] = "AudioStream",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Converts a document.</summary>
    /// <param name="document">The .xscn or .xres model.</param>
    /// <param name="metadata">The engine classes, for the declared type of a property holding a file.</param>
    /// <param name="options">The seed or kept ids, the host's lookups and the header's uid.</param>
    /// <returns>The .tscn or .tres model, its issues and the ids its sections got.</returns>
    public static ConversionResult<TscnDocument> Convert(XscnDocument document, GodotMetadata metadata, ConversionOptions options) =>
        Convert(document, metadata, options, SiteHash);

    // siteHash gives the hash of a sub-resource's site key, which its new id is seeded from.
    internal static ConversionResult<TscnDocument> Convert(
        XscnDocument document,
        GodotMetadata metadata,
        ConversionOptions options,
        Func<string, ulong> siteHash
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        var context = new Context(document, metadata, options, siteHash);
        TscnDocument converted = document.Kind == XscnDocumentKind.Scene ? context.ConvertScene() : context.ConvertResource();
        return new ConversionResult<TscnDocument>(converted, [.. context.Issues]) { Ids = context.Ids() };
    }

    // FNV-1a 64 over the key's UTF-8 bytes: the same on every run and platform, which string.GetHashCode is not.
    private static ulong SiteHash(string siteKey)
    {
        ulong hash = 0xCBF29CE484222325UL;
        foreach (byte octet in System.Text.Encoding.UTF8.GetBytes(siteKey))
        {
            hash ^= octet;
            hash *= 0x100000001B3UL;
        }

        return hash;
    }

    private sealed partial class Context
    {
        private readonly XscnDocument document;
        private readonly GodotMetadata metadata;
        private readonly ConversionOptions options;
        private readonly Func<string, ulong> siteHash;
        private readonly PropertyOrder order;
        private readonly InstancedClasses? instancedClasses;
        private readonly HashSet<(string Reference, string Reason)> unreadableScenes = [];
        private readonly Dictionary<string, XscnResource> table = new(StringComparer.Ordinal);
        private readonly List<ExtUse> extUses = [];
        private readonly HashSet<string> extSeen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> textUids = new(StringComparer.Ordinal);
        private readonly List<SubUse> subUses = [];
        private readonly HashSet<XscnResource> visiting = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<XscnResource> found = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, string> extIds = new(StringComparer.Ordinal);
        private readonly Dictionary<XscnResource, string> subIds = new(ReferenceEqualityComparer.Instance);

        public Context(XscnDocument document, GodotMetadata metadata, ConversionOptions options, Func<string, ulong> siteHash)
        {
            this.document = document;
            this.metadata = metadata;
            this.options = options;
            this.siteHash = siteHash;
            order = new PropertyOrder(metadata);
            instancedClasses = options.SceneFiles is { } files && options.SourceKey is { } key ? new InstancedClasses(metadata, files, key) : null;
            IndexTable();
        }

        public List<ConversionIssue> Issues { get; } = [];

        public ConversionIds Ids() => new(extIds, subIds);

        // An entry without a key cannot be referred to, and a repeated key names the first entry with it.
        private void IndexTable()
        {
            foreach (XscnResource entry in document.Resources)
            {
                if (entry.Key is null)
                {
                    Report(
                        ConversionCodes.MissingKey,
                        ConversionSeverity.Error,
                        "This x:Resources entry has no x:Key; it is dropped.",
                        "x:Resources"
                    );
                }
                else if (!table.TryAdd(entry.Key, entry))
                {
                    string message = $"An earlier x:Resources entry has the key '{entry.Key}'; this one is dropped.";
                    Report(ConversionCodes.RepeatedKey, ConversionSeverity.Error, message, $"x:Resources \"{entry.Key}\"");
                }
            }
        }

        // _find_resources (resource_format_text.cpp:1680-1714): a resource's properties in sorted name order, each
        // reference followed where it is met. The site is the site key of the element or resource that holds them: a
        // resource held inline gets "<holder's site>|<property>", and a table entry "key:<x:Key>" wherever it is used.
        private void Discover(XscnResource resource, string location, string site) =>
            DiscoverProperties(resource.Type, resource.Properties, location, site);

        private void DiscoverProperties(XscnType? owner, ImmutableArray<XscnProperty> properties, string location, string site) =>
            DiscoverInOrder(owner, properties.OrderBy(property => property.GodotName, StringComparer.Ordinal), location, site);

        // The properties' references followed in the order given: a resource's sorted, a node's as its section writes them.
        private void DiscoverInOrder(XscnType? owner, IEnumerable<XscnProperty> properties, string location, string site)
        {
            foreach (XscnProperty property in properties)
            {
                string name = property.GodotName;
                DiscoverValue(property.Value, DeclaredType(owner, name), location + "." + name, site + "|" + name);
            }
        }

        private void DiscoverValue(XscnValue value, string? declaredType, string location, string site)
        {
            switch (value)
            {
                case XscnFile file:
                    DiscoverFile(file.Path, declaredType, null, location);
                    break;
                case XscnKeyRef key:
                    DiscoverKey(key.Key, location);
                    break;
                case XscnInline inline:
                    DiscoverResource(inline.Resource, location, site);
                    break;
                case XscnScalar scalar:
                    string? bareType = scalar.Value is GodotPathRef ? declaredType : null;
                    ValueReferences.Visit(scalar.Value, reference => DiscoverReference(reference, bareType, location));
                    break;
            }
        }

        // declaredType is the property's when the reference is its whole value.
        private void DiscoverReference(GodotValue reference, string? declaredType, string location)
        {
            switch (reference)
            {
                case GodotPathRef { Path: { } path } pathRef:
                    DiscoverFile(path, declaredType, pathRef.Uid, location);
                    break;
                case GodotPathRef { Uid: var uid }:
                    string uidMessage = $"Resource(\"{uid}\") has no path, and an ext_resource needs one; it is kept as it stands.";
                    Report(ConversionCodes.UidWithoutPath, ConversionSeverity.Error, uidMessage, location);
                    break;
                case GodotResourceRef { Kind: GodotRefKind.Sub } sub:
                    DiscoverKey(sub.Id, location);
                    break;
                case GodotResourceRef { Kind: GodotRefKind.External } ext:
                    string message = $"ExtResource(\"{ext.Id}\") names no section in .xscn; refer to the file as Resource(\"res://…\").";
                    Report(ConversionCodes.ExtResourceInText, ConversionSeverity.Error, message, location);
                    break;
            }
        }

        // resource_format_text.cpp:1669: an external resource is numbered when it is first met. A uid written with the
        // path in value text is the file's uid when x:Uids has none.
        private void DiscoverFile(string path, string? declaredType, string? uid, string location)
        {
            if (extSeen.Add(path))
            {
                extUses.Add(new ExtUse(path, declaredType, location));
            }

            if (uid is not null)
            {
                textUids.TryAdd(path, uid);
            }
        }

        private void DiscoverKey(string key, string location)
        {
            if (table.TryGetValue(key, out XscnResource? entry))
            {
                DiscoverResource(entry, $"x:Resources \"{key}\"", "key:" + key);
            }
            else
            {
                Report(ConversionCodes.UnknownKey, ConversionSeverity.Error, $"No x:Resources entry has the key '{key}'.", location);
            }
        }

        // resource_format_text.cpp:1674-1714: a resource is saved after the resources it uses, once.
        private void DiscoverResource(XscnResource resource, string location, string site)
        {
            if (found.Contains(resource))
            {
                return;
            }

            if (!visiting.Add(resource))
            {
                string message = "This resource refers back to itself through others, which Godot cannot load; the loop is not followed.";
                Report(ConversionCodes.ResourceCycle, ConversionSeverity.Error, message, location);
                return;
            }

            Discover(resource, location, site);
            visiting.Remove(resource);
            found.Add(resource);
            subUses.Add(new SubUse(resource, location, site));
        }

        private string? DeclaredType(XscnType? owner, string property) =>
            owner is XscnClassType { Class: var owningClass } && metadata.FindProperty(owningClass, property) is { ClassName.Length: > 0 } info
                ? CommonClass(info.ClassName)
                : null;

        // A property may take any of several classes (PrimitiveMesh.material: "BaseMaterial3D,ShaderMaterial"); the
        // declared type is then the nearest class all of them inherit from, or null when the metadata knows none.
        private string? CommonClass(string classNames)
        {
            string[] names = classNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 1)
            {
                return names[0];
            }

            GodotClassInfo?[] classes = [.. names.Select(metadata.FindClass)];
            if (classes.Any(found => found is null))
            {
                return null;
            }

            return Ancestors(classes[0]!).FirstOrDefault(ancestor => classes.All(found => metadata.Inherits(found!, ancestor)));
        }

        private IEnumerable<string> Ancestors(GodotClassInfo start)
        {
            for (GodotClassInfo? current = start; current is not null; current = current.Parent is { } parent ? metadata.FindClass(parent) : null)
            {
                yield return current.Name;
            }
        }

        // The ids, ext_resources numbered in discovery order and written in Godot's natural id order
        // (resource_format_text.cpp:1868-1877), sub_resources after what they use; the sections get their properties
        // once every id is known. A new sub_resource id is seeded from its site key, so an edit elsewhere in the file
        // leaves it as it was; one that would repeat an earlier id takes the next free one.
        private TscnDocument MintIds(TscnDocument shell)
        {
            TscnDocument result = MintExtIds(shell);
            HashSet<string> taken = new(result.SubResources.Select(sub => sub.Id), StringComparer.Ordinal);
            ImmutableArray<TscnSubResource>.Builder minted = ImmutableArray.CreateBuilder<TscnSubResource>(
                result.SubResources.Length + subUses.Count
            );
            minted.AddRange(result.SubResources);
            foreach (SubUse use in subUses)
            {
                string type = TypeName(use.Resource.Type, use.Location);
                string id = options.KeepIds?.SubIdOf(use.Resource) ?? TscnDocument.NewSubResourceId(type, options.Seed ^ siteHash(use.Site), taken);
                subIds[use.Resource] = id;
                taken.Add(id);
                minted.Add(new TscnSubResource { Type = type, Id = id });
            }

            result = result with { SubResources = minted.ToImmutable() };
            return result with
            {
                ExtResources = [.. result.ExtResources.OrderBy(ext => ext.Id, GodotNaturalOrder.NoCase)],
                SubResources = [.. result.SubResources.Select((sub, i) => sub with { Properties = Properties(subUses[i]) })],
            };
        }

        private TscnDocument MintExtIds(TscnDocument shell)
        {
            TscnDocument result = shell;
            for (int i = 0; i < extUses.Count; i++)
            {
                ExtUse use = extUses[i];
                string id = options.KeepIds?.ExtIdOf(use.Path) ?? result.NewExtResourceId(SeedOf(i));
                extIds[use.Path] = id;
                WarnOnDisagreeingUids(use.Path);
                var ext = new TscnExtResource
                {
                    Type = ExtType(use),
                    Uid = UidOf(use.Path),
                    Path = use.Path,
                    Id = id,
                };
                result = result with { ExtResources = result.ExtResources.Add(ext) };
            }

            return result;
        }

        // x:Uids first, else a uid written with the path in value text.
        private string? UidOf(string path) => document.Uids.FirstOrDefault(uid => uid.Path == path)?.Uid ?? textUids.GetValueOrDefault(path);

        // Each distinct uid the path's x:Uids entries give, in first-seen order; two make the ext_resource's choice worth
        // a warning, and it keeps the first.
        private void WarnOnDisagreeingUids(string path)
        {
            string[] uids = [.. document.Uids.Where(uid => uid.Path == path).Select(uid => uid.Uid).Distinct(StringComparer.Ordinal)];
            if (uids.Length < 2)
            {
                return;
            }

            string message = $"x:Uids gives '{path}' more than one uid ({string.Join(", ", uids)}); its ext_resource keeps '{uids[0]}'.";
            Report(ConversionCodes.UidsDisagree, ConversionSeverity.Warning, message, $"x:Uid \"{path}\"");
        }

        private ulong SeedOf(int ordinal) => options.Seed ^ ((ulong)(ordinal + 1) * 0x9E3779B97F4A7C15UL);

        // The host's lookup, the declared type of the property that first used the file, the table by extension.
        private string ExtType(ExtUse use)
        {
            string? type =
                options.ExtResourceTypes?.ClassOf(use.Path) ?? use.DeclaredType ?? TypesByExtension.GetValueOrDefault(Path.GetExtension(use.Path));
            if (type is not null)
            {
                return type;
            }

            string message = $"Nothing says what class '{use.Path}' holds; its ext_resource says type=\"Resource\".";
            Report(ConversionCodes.UnknownExtResourceType, ConversionSeverity.Warning, message, use.Location);
            return "Resource";
        }

        private string TypeName(XscnType type, string location)
        {
            switch (type)
            {
                case XscnClassType classType:
                    return classType.Class.Name;
                case XscnUnknownType unknown:
                    return unknown.Name;
                default:
                    string message = "A resource's tag names a scene; it is written as a Resource.";
                    Report(ConversionCodes.NotAResource, ConversionSeverity.Error, message, location);
                    return "Resource";
            }
        }

        private ImmutableArray<TscnProperty> Properties(SubUse use) => Properties(use.Resource, use.Location);

        private ImmutableArray<TscnProperty> Properties(XscnResource resource, string location) =>
            Properties(resource.Type, resource.Properties, location);

        // owner says the section's class, which sets the property order; anything but a class keeps source order.
        private ImmutableArray<TscnProperty> Properties(XscnType? owner, ImmutableArray<XscnProperty> source, string location) =>
            Properties((owner as XscnClassType)?.Class, source, location);

        private ImmutableArray<TscnProperty> Properties(GodotClassInfo? cls, ImmutableArray<XscnProperty> source, string location)
        {
            ImmutableArray<TscnProperty>.Builder properties = ImmutableArray.CreateBuilder<TscnProperty>();
            foreach (XscnProperty property in order.Sort(cls, source))
            {
                if (Value(property.Value, location + "." + property.GodotName) is { } value)
                {
                    properties.Add(new TscnProperty(property.GodotName, value));
                }
            }

            return properties.ToImmutable();
        }

        // Null drops the property: an invalid value, or a key the discovery already reported as unknown.
        private GodotValue? Value(XscnValue value, string location) =>
            value switch
            {
                XscnScalar scalar => ValueReferences.Map(scalar.Value, Rewrite),
                XscnFile file => new GodotResourceRef(GodotRefKind.External, extIds[file.Path]),
                XscnKeyRef key => table.TryGetValue(key.Key, out XscnResource? entry) ? SubRef(entry) : null,
                XscnInline inline => SubRef(inline.Resource),
                XscnInvalid invalid => Invalid(invalid, location),
                _ => throw new InvalidOperationException($"No conversion for a {value.GetType().Name}."),
            };

        private GodotResourceRef SubRef(XscnResource resource) => new(GodotRefKind.Sub, subIds[resource]);

        private GodotValue Rewrite(GodotValue reference) =>
            reference switch
            {
                GodotPathRef { Path: { } path } when extIds.TryGetValue(path, out string? id) => new GodotResourceRef(GodotRefKind.External, id),
                GodotResourceRef { Kind: GodotRefKind.Sub } sub when table.TryGetValue(sub.Id, out XscnResource? entry) => SubRef(entry),
                _ => reference,
            };

        private GodotValue? Invalid(XscnInvalid invalid, string location)
        {
            string message = $"The value '{invalid.Text}' could not be read ({invalid.Message}); the property is dropped.";
            Report(ConversionCodes.InvalidValue, ConversionSeverity.Error, message, location);
            return null;
        }

        private void Report(string code, ConversionSeverity severity, string message, string location) =>
            Issues.Add(new ConversionIssue(code, severity, message, location));

        private sealed record ExtUse(string Path, string? DeclaredType, string Location);

        // Location names the use in messages; Site is the site key the new id is seeded from.
        private sealed record SubUse(XscnResource Resource, string Location, string Site);
    }
}
