using System.Collections.Immutable;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>What <see cref="XscnPathRewriter.Rewrite"/> gives: the document with its paths rewritten, and what it left as it was.</summary>
/// <param name="Document">The rewritten document; the input document is not changed.</param>
/// <param name="Warnings">A line for each path held in unparsed text, in document order.</param>
public sealed record XscnPathRewriteResult(XscnDocument Document, ImmutableArray<string> Warnings);

/// <summary>
/// Rewrites the <c>res://</c> paths a document refers to from a map of old path to new path, so a converted or moved file
/// is still found: every site the model holds a path in, then the scene namespaces the paths' folders name.
/// </summary>
public static class XscnPathRewriter
{
    /// <summary>Rewrites every path the document holds that the map gives a new path for.</summary>
    /// <param name="document">The document to rewrite; it is not changed.</param>
    /// <param name="renames">Old <c>res://</c> path to new <c>res://</c> path; a path matches whole and ordinal.</param>
    /// <returns>The rewritten document, and the unparsed text that holds a path it could not rewrite.</returns>
    public static XscnPathRewriteResult Rewrite(XscnDocument document, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renames);
        var session = new Session(document, new Dictionary<string, string>(renames, StringComparer.Ordinal));
        return session.Run();
    }

    // One rewrite: the warnings collected on the way, and the namespaces rebuilt from the scene folders it leaves.
    private sealed class Session(XscnDocument document, Dictionary<string, string> renames)
    {
        private readonly List<string> warnings = [];

        public XscnPathRewriteResult Run()
        {
            ImmutableArray<(string Old, string New)> scenes = [.. SceneFolders(document.Root)];
            // The tables come before the root in the text, so their warnings come first.
            ImmutableArray<XscnResource> resources = [.. document.Resources.Select(resource => RewriteResource(resource, TableLocation(resource)))];
            XscnElement root = document.Root switch
            {
                XscnNodeElement element => RewriteNode(element, NodeLocation(element)),
                XscnResource resource => RewriteResource(resource, "root"),
                _ => throw new ArgumentException($"The root is a {document.Root.GetType().Name}, not a node or a resource.", nameof(document)),
            };
            return new XscnPathRewriteResult(
                document with
                {
                    Namespaces = Namespaces(scenes),
                    Uids = [.. document.Uids.Select(uid => uid with { Path = Mapped(uid.Path) ?? uid.Path })],
                    Resources = resources,
                    Root = root,
                },
                [.. warnings]
            );
        }

        // Every scene path in pre-order as its folder before and after the rewrite, which the namespaces are rebuilt from.
        private IEnumerable<(string Old, string New)> SceneFolders(XscnElement element) =>
            element switch
            {
                XscnNodeElement node => FolderOf((node as XscnNode)?.Type).Concat(node.Children.SelectMany(SceneFolders)),
                XscnResource resource => FolderOf(resource.Type),
                _ => [],
            };

        private IEnumerable<(string Old, string New)> FolderOf(XscnType? type)
        {
            if (type is XscnSceneType scene && scene.Path.StartsWith("res://", StringComparison.Ordinal))
            {
                yield return (FolderOf(scene.Path), FolderOf(Mapped(scene.Path) ?? scene.Path));
            }
        }

        private static string FolderOf(string path) => path[..(path.LastIndexOf('/') + 1)];

        // A scene namespace is kept when its folder still names a scene, retargeted when all its scenes moved to one folder,
        // and dropped otherwise; a folder no namespace serves gets a new one, its prefix free among those kept.
        private ImmutableArray<XscnNamespace> Namespaces(ImmutableArray<(string Old, string New)> scenes)
        {
            List<string> folders = [.. scenes.Select(scene => scene.New).Distinct(StringComparer.Ordinal)];
            var claims = new NamespaceClaims(folders);
            var kept = new List<XscnNamespace>(KeptNamespaces(scenes, claims));
            kept.AddRange(Minted(folders, claims));
            return [.. kept];
        }

        // Every namespace whose folder still names a scene keeps it whoever declared it, before an unused one may take a
        // folder it no longer names; the ones kept keep the document's order.
        private IEnumerable<XscnNamespace> KeptNamespaces(ImmutableArray<(string Old, string New)> scenes, NamespaceClaims claims)
        {
            var kept = new XscnNamespace?[document.Namespaces.Length];
            for (int index = 0; index < document.Namespaces.Length; index++)
            {
                kept[index] = KeptByFolder(document.Namespaces[index], claims);
            }

            for (int index = 0; index < document.Namespaces.Length; index++)
            {
                kept[index] ??= Retargeted(document.Namespaces[index], scenes, claims);
            }

            return kept.OfType<XscnNamespace>();
        }

        private static XscnNamespace? KeptByFolder(XscnNamespace scope, NamespaceClaims claims) =>
            claims.Uses(scope.Folder) && claims.Claim(scope.Folder, scope.Prefix) ? scope : null;

        private static XscnNamespace? Retargeted(XscnNamespace scope, ImmutableArray<(string Old, string New)> scenes, NamespaceClaims claims)
        {
            string[] moved = [.. scenes.Where(scene => scene.Old == scope.Folder).Select(scene => scene.New).Distinct(StringComparer.Ordinal)];
            return moved.Length == 1 && claims.Claim(moved[0], scope.Prefix) ? scope with { Folder = moved[0] } : null;
        }

        private static IEnumerable<XscnNamespace> Minted(List<string> folders, NamespaceClaims claims)
        {
            foreach (string folder in folders)
            {
                if (claims.Claim(folder))
                {
                    yield return new XscnNamespace(TscnToXscn.FreeFolderPrefix(TscnToXscn.FolderPrefix(folder), claims.Prefixes), folder);
                }
            }
        }

        private XscnNodeElement RewriteNode(XscnNodeElement element, string location)
        {
            ImmutableArray<XscnProperty> properties = [.. element.Properties.Select(property => RewriteProperty(property, location))];
            ImmutableArray<XscnConnection> connections = [.. element.Connections.Select(RewriteConnection)];
            ImmutableArray<XscnNodeElement> children = [.. element.Children.Select(child => RewriteNode(child, ChildLocation(location, child)))];
            string? placeholder = element.InstancePlaceholder is { } path ? Mapped(path) ?? path : null;
            return element switch
            {
                XscnNode node => node with
                {
                    Type = RewriteType(node.Type),
                    Properties = properties,
                    Connections = connections,
                    Children = children,
                    InstancePlaceholder = placeholder,
                },
                XscnOverride change => change with
                {
                    Properties = properties,
                    Connections = connections,
                    Children = children,
                    InstancePlaceholder = placeholder,
                },
                _ => throw new ArgumentException($"An element is a {element.GetType().Name}, not a node or an override.", nameof(element)),
            };
        }

        private XscnResource RewriteResource(XscnResource resource, string location) =>
            resource with
            {
                Type = RewriteType(resource.Type),
                Properties = [.. resource.Properties.Select(property => RewriteProperty(property, location))],
            };

        private XscnProperty RewriteProperty(XscnProperty property, string location) =>
            property with
            {
                Value = RewriteValue(property.Value, location),
            };

        private XscnValue RewriteValue(XscnValue value, string location) =>
            value switch
            {
                XscnFile file => Mapped(file.Path) is { } path ? file with { Path = path } : file,
                XscnScalar scalar => RewriteScalar(scalar),
                XscnInline inline => inline with { Resource = RewriteResource(inline.Resource, location) },
                XscnInvalid invalid => WarnInvalid(invalid, location),
                _ => value,
            };

        private XscnScalar RewriteScalar(XscnScalar scalar)
        {
            GodotValue mapped = ValueReferences.Map(scalar.Value, RewriteReference);
            return ReferenceEquals(mapped, scalar.Value) ? scalar : new XscnScalar(mapped);
        }

        private XscnConnection RewriteConnection(XscnConnection connection) =>
            connection with
            {
                Binds = [.. connection.Binds.Select(bind => ValueReferences.Map(bind, RewriteReference))],
            };

        // A path reference keeps its uid; the path is the only part a rename changes.
        private GodotValue RewriteReference(GodotValue value) =>
            value is GodotPathRef { Path: { } path } reference && Mapped(path) is { } mapped ? new GodotPathRef(reference.Uid, mapped) : value;

        private XscnType RewriteType(XscnType type) => type is XscnSceneType scene && Mapped(scene.Path) is { } path ? new XscnSceneType(path) : type;

        // Unparsed text cannot be rewritten safely: each path it holds is a warning, and the text stays as it is.
        private XscnInvalid WarnInvalid(XscnInvalid invalid, string location)
        {
            foreach (string oldPath in renames.Keys)
            {
                if (invalid.Text.Contains(oldPath, StringComparison.Ordinal))
                {
                    warnings.Add($"{location}: unparsed text holds {oldPath}; it is not rewritten.");
                }
            }

            return invalid;
        }

        private string? Mapped(string path) => renames.TryGetValue(path, out string? mapped) ? mapped : null;

        private static string NodeLocation(XscnNodeElement element) =>
            element switch
            {
                XscnNode node => node.Name,
                XscnOverride change => change.Path,
                _ => "",
            };

        private static string ChildLocation(string location, XscnNodeElement child) => location + "/" + NodeLocation(child);

        private static string TableLocation(XscnResource resource) => resource.Key is { } key ? "x:Resources[" + key + "]" : "x:Resources";

        // The folders the document's scenes name, and the folders and prefixes the rebuilt namespaces have taken so far.
        private sealed class NamespaceClaims(IEnumerable<string> used)
        {
            private readonly HashSet<string> usedFolders = new(used, StringComparer.Ordinal);
            private readonly HashSet<string> claimedFolders = new(StringComparer.Ordinal);

            public HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);

            public bool Uses(string folder) => usedFolders.Contains(folder);

            public bool Claim(string folder) => claimedFolders.Add(folder);

            public bool Claim(string folder, string prefix)
            {
                Prefixes.Add(prefix);
                return claimedFolders.Add(folder);
            }
        }
    }
}
