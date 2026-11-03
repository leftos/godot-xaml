using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>A scene the class lookup could not read: the reference as its referrer writes it, and why.</summary>
/// <param name="Reference">The reference that failed, which may be one inside another instanced scene.</param>
/// <param name="Reason">
/// <c>not found</c>, <c>line L, column C: …</c> for a parse error, <c>a cycle</c>, or <c>no node at "path"</c>.
/// </param>
internal sealed record InstancedFailure(string Reference, string Reason);

/// <summary>What the class lookup of an instanced node found.</summary>
/// <param name="Class">The node's class; null when there is no answer.</param>
/// <param name="Failure">
/// Why there is no answer when a scene could not be read; null with no class means the class is one the metadata does
/// not know, or a scene that is not text.
/// </param>
internal readonly record struct InstancedClass(GodotClassInfo? Class, InstancedFailure? Failure)
{
    public static InstancedClass Unknown => new(null, null);

    public static InstancedClass Failed(string reference, string reason) => new(null, new InstancedFailure(reference, reason));
}

/// <summary>
/// Reads the class of a node inside an instanced scene from the scene's text, which the host gives: a typed node's class,
/// an instanced or inherited one's from its own scene, and a node below an instance from that instance's scene. Only
/// <c>.tscn</c> and <c>.xscn</c> are read; an imported model or a binary scene has no class here. Each reference is read
/// and each file parsed once per conversion; nothing outlives it.
/// </summary>
internal sealed class InstancedClasses(GodotMetadata metadata, ISceneFiles files, string sourceKey)
{
    private const string UidScheme = "uid://";
    private const string NotFound = "not found";

    private readonly Dictionary<(string Referrer, string Reference, string? Uid), SceneFile?> reads = [];
    private readonly Dictionary<string, Parsed> parsed = new(StringComparer.Ordinal);
    private readonly HashSet<string> visiting = new(StringComparer.Ordinal);

    /// <summary>The class of a node of a scene the converted file refers to.</summary>
    /// <param name="reference">The scene's path as the converted file writes it.</param>
    /// <param name="uid">The uid the converted file gives the reference, or null.</param>
    /// <param name="path">The node's path inside the scene, <c>.</c> for its root.</param>
    /// <returns>The class, or which of the two no-answers it is.</returns>
    public InstancedClass ClassOf(string reference, string? uid, string path) => ClassOf(sourceKey, new SceneRef(reference, uid), path);

    private static bool IsTextScene(string reference) =>
        Path.GetExtension(reference) is { } extension
        && (extension.Equals(".tscn", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xscn", StringComparison.OrdinalIgnoreCase));

    private InstancedClass ClassOf(string referrer, SceneRef scene, string path)
    {
        if (scene.Reference.StartsWith(UidScheme, StringComparison.Ordinal))
        {
            return InstancedClass.Failed(scene.Reference, NotFound);
        }

        if (!IsTextScene(scene.Reference))
        {
            return InstancedClass.Unknown;
        }

        if (Read(referrer, scene) is not { } file)
        {
            return InstancedClass.Failed(scene.Reference, NotFound);
        }

        Parsed nodes = ParsedOf(file);
        if (nodes.Error is { } error)
        {
            return InstancedClass.Failed(scene.Reference, error);
        }

        if (!visiting.Add(file.Key))
        {
            return InstancedClass.Failed(scene.Reference, "a cycle");
        }

        try
        {
            return Resolve(file.Key, scene.Reference, nodes.Nodes, path);
        }
        finally
        {
            visiting.Remove(file.Key);
        }
    }

    private SceneFile? Read(string referrer, SceneRef scene)
    {
        (string, string, string?) key = (referrer, scene.Reference, scene.Uid);
        if (!reads.TryGetValue(key, out SceneFile? file))
        {
            file = files.Read(referrer, scene.Reference, scene.Uid);
            reads.Add(key, file);
        }

        return file;
    }

    // A node the scene types answers itself; below an instance the scene holds, the instance's scene answers.
    private InstancedClass Resolve(string key, string reference, Dictionary<string, SceneNode> nodes, string path)
    {
        if (nodes.TryGetValue(path, out SceneNode node))
        {
            return node switch
            {
                { Class: { } cls } => new InstancedClass(cls, null),
                { Scene: { } scene } => ClassOf(key, scene, TscnNodePaths.Root),
                _ => InstancedClass.Unknown,
            };
        }

        for (string prefix = path; prefix != TscnNodePaths.Root; )
        {
            prefix = TscnNodePaths.ParentOf(prefix);
            if (nodes.TryGetValue(prefix, out SceneNode above) && above.Scene is { } scene)
            {
                string below = prefix == TscnNodePaths.Root ? path : path[(prefix.Length + 1)..];
                return ClassOf(key, scene, below);
            }
        }

        return InstancedClass.Failed(reference, $"no node at \"{path}\"");
    }

    private Parsed ParsedOf(SceneFile file)
    {
        if (!parsed.TryGetValue(file.Key, out Parsed? nodes))
        {
            nodes = Parse(file);
            parsed.Add(file.Key, nodes);
        }

        return nodes;
    }

    // A file that does not parse gives the reason the core's readers give: a parse error or a refusal.
    private Parsed Parse(SceneFile file)
    {
        try
        {
            return file.Key.EndsWith(".xscn", StringComparison.OrdinalIgnoreCase) ? FromXscn(file.Text) : FromTscn(file.Text);
        }
        catch (TscnParseException exception)
        {
            return Parsed.Failed($"line {exception.Line}, column {exception.Column}: {exception.Reason}");
        }
        catch (Exception exception) when (exception is NotSupportedException or (ArgumentException and not ArgumentNullException))
        {
            return Parsed.Failed(exception.Message);
        }
    }

    private Parsed FromXscn(string text)
    {
        XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, metadata);
        if (result.Diagnostics.FirstOrDefault(d => d.Severity == XscnSeverity.Error) is { } error)
        {
            LinePosition position = new LineMap(text).OneBased(error.Span.Start);
            return Parsed.Failed($"line {position.Line}, column {position.Column}: {error.Message}");
        }

        var nodes = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        if (result.Document.Root is XscnNode root)
        {
            var uids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XscnUid uid in result.Document.Uids)
            {
                uids.TryAdd(uid.Path, uid.Uid);
            }

            AddXscn(root, null, uids, nodes);
        }

        return new Parsed(nodes, null);
    }

    // The same paths the way back gives: a node under its parent, an override at its path under its instance.
    private static void AddXscn(XscnNodeElement element, string? parent, Dictionary<string, string> uids, Dictionary<string, SceneNode> nodes)
    {
        string path;
        if (element is XscnNode node)
        {
            path = TscnNodePaths.PathOf(parent, node.Name);
            nodes.TryAdd(path, NodeOf(node, uids));
        }
        else
        {
            string over = ((XscnOverride)element).Path;
            path = parent is null or TscnNodePaths.Root ? over : parent + "/" + over;
        }

        foreach (XscnNodeElement child in element.Children)
        {
            AddXscn(child, path, uids, nodes);
        }
    }

    private static SceneNode NodeOf(XscnNode node, Dictionary<string, string> uids) =>
        node switch
        {
            { InstancePlaceholder: not null } => default,
            { Type: XscnClassType known } => new SceneNode(known.Class, null),
            { Type: XscnSceneType scene } => new SceneNode(null, new SceneRef(scene.Path, uids.GetValueOrDefault(scene.Path))),
            _ => default,
        };

    private Parsed FromTscn(string text)
    {
        TscnDocument document = TscnReader.Read(text);
        var nodes = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        foreach (TscnNode node in document.Nodes)
        {
            SceneNode? found = node switch
            {
                { InstancePlaceholder: not null } => default(SceneNode),
                { Type: { } type } => new SceneNode(metadata.FindClass(type), null),
                { Instance: GodotResourceRef { Kind: GodotRefKind.External } instance } => SceneOf(document, instance.Id),
                _ => null,
            };
            if (found is { } scene)
            {
                nodes.TryAdd(TscnNodePaths.PathOf(node.Parent, node.Name), scene);
            }
        }

        return new Parsed(nodes, null);
    }

    private static SceneNode? SceneOf(TscnDocument document, string id) =>
        document.ExtResources.FirstOrDefault(ext => ext.Id == id) is { } ext ? new SceneNode(null, new SceneRef(ext.Path, ext.Uid)) : null;

    // A reference to a scene as a file writes it, with the uid the file gives it.
    private sealed record SceneRef(string Reference, string? Uid);

    // A node of a scene: its class, or the scene it instances; neither for a class the metadata does not know.
    private readonly record struct SceneNode(GodotClassInfo? Class, SceneRef? Scene);

    // A file's nodes by path, or why it does not parse.
    private sealed record Parsed(Dictionary<string, SceneNode> Nodes, string? Error)
    {
        public static Parsed Failed(string error) => new(new Dictionary<string, SceneNode>(StringComparer.Ordinal), error);
    }
}
