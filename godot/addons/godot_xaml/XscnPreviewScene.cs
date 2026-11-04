using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;
using ObjectValue = GodotXaml.Core.Values.GodotObject;

namespace GodotXaml.Addon;

/// <summary>What a preview of <c>.xscn</c> text gave.</summary>
/// <param name="Root">
/// The instantiated scene, which the caller owns and frees; null when the text has a parse error, the conversion an error,
/// a stripped preview cannot read or strip a scene the text instances (an issue names it), or Godot could not load or
/// instantiate the converted text (its reason is then in Godot's log).
/// </param>
/// <param name="Diagnostics">The parse's diagnostics, never pushed to Godot's log.</param>
/// <param name="Issues">The conversion's issues, never pushed to Godot's log; empty when the text did not parse.</param>
/// <param name="Document">
/// The parsed document, whose elements' spans locate each node's source (<see cref="XscnPreviewRects"/>); null when the
/// text did not parse.
/// </param>
internal sealed record PreviewScene(
    Node? Root,
    ImmutableArray<XscnDiagnostic> Diagnostics,
    ImmutableArray<ConversionIssue> Issues,
    XscnDocument? Document
);

/// <summary>
/// Turns <c>.xscn</c> text, saved or not, into an instantiated scene for the preview server: parsed, converted, loaded
/// through Godot's text loader with the cache ignored, and instantiated. The text is written only to the load route's own
/// temp file, and a stripped preview's instanced scenes only to their copies in the same folder, never under the project.
/// Main thread only: it calls Godot.
/// </summary>
internal static class XscnPreviewScene
{
    /// <summary>The project setting that lets a request keep the scene's scripts; off when unset.</summary>
    internal const string AllowScriptsSetting = "godot_xaml/preview_server/allow_scripts";

    private const string ScriptProperty = "script";

    // The base of every script class; outside the editor the conversion types a script's ext_resource with it.
    private const string ScriptClass = "Script";

    private static readonly GodotTypeRef ObjectType = new("Object");

    /// <summary>Parses, converts, loads and instantiates the text as the scene at <paramref name="resPath"/>.</summary>
    /// <param name="text">The <c>.xscn</c> text.</param>
    /// <param name="resPath">The document's <c>res://</c> path, which relative references resolve against.</param>
    /// <param name="scriptsRequested">
    /// Whether the request asks for the scene's scripts; they are kept only when <see cref="AllowScriptsSetting"/> is on too.
    /// </param>
    /// <returns>The root, or null, with the parse's diagnostics and the conversion's issues.</returns>
    internal static PreviewScene Instantiate(string text, string resPath, bool scriptsRequested)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, XscnMetadata.Value);
        if (parsed.HasErrors)
        {
            return new PreviewScene(null, parsed.Diagnostics, [], null);
        }

        ConversionResult<TscnDocument> result = XscnLoading.ToTscn(parsed.Document, resPath);
        if (result.Issues.Any(issue => issue.Severity == ConversionSeverity.Error))
        {
            return new PreviewScene(null, parsed.Diagnostics, result.Issues, parsed.Document);
        }

        // The text loader reads the temp file, so each path is made a resource path against the document's, as a load does.
        TscnDocument converted = result.Document with
        {
            ExtResources = [.. result.Document.ExtResources.Select(ext => ext with { Path = XscnLoading.Localized(resPath, ext.Path) })],
        };
        if (scriptsRequested && ScriptsAllowed())
        {
            return new PreviewScene(LoadAndInstantiate(resPath, converted), parsed.Diagnostics, result.Issues, parsed.Document);
        }

        // Each scene the text instances, at any depth, is loaded from a stripped copy, deleted once the load is done.
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(converted);
        return instances.Issues.Count > 0
            ? new PreviewScene(null, parsed.Diagnostics, [.. result.Issues, .. instances.Issues], parsed.Document)
            : new PreviewScene(LoadAndInstantiate(resPath, stripped), parsed.Diagnostics, result.Issues, parsed.Document);
    }

    private static bool ScriptsAllowed()
    {
        using Variant fallback = false;
        using Variant setting = ProjectSettings.GetSetting(AllowScriptsSetting, fallback);
        return setting.AsBool();
    }

    // The load route names the sub-resources "<resPath>::<id>" without caching them in this mode. The scene's wrapper is
    // disposed once instantiated, so the scene is freed with the last reference; the nodes do not hold it.
    private static Node? LoadAndInstantiate(string resPath, TscnDocument converted)
    {
        using Variant loaded = XscnLoadRoute.Load(
            resPath,
            TscnWriter.Write(converted),
            isScene: true,
            "PackedScene",
            ResourceFormatLoader.CacheMode.Ignore
        );
        if (loaded.VariantType != Variant.Type.Object || loaded.AsGodotObject() is not PackedScene scene)
        {
            return null;
        }

        using (scene)
        {
            return scene.Instantiate(PackedScene.GenEditState.Disabled);
        }
    }

    /// <summary>
    /// The document with no script from its own text: no <c>script</c> property that names a script (an explicit null
    /// stays), no Script-class sub-resource, no ext_resource whose resolved file is a script
    /// (<see cref="XscnPreviewReferences.Classify(TscnExtResource, Func{string, PreviewReferenceKind})"/>) or whose type is a
    /// Script class, no tag field the model does not hold, and no connection from or to a node whose script was removed.
    /// Every other property stays: Godot sets each stored node property with <c>node->set</c> and is silent on one the class
    /// lacks (packed_scene.cpp:372-383), so a script's exported values stay where the class has them. The scenes the
    /// document instances are stripped apart (<see cref="XscnPreviewInstances"/>).
    /// </summary>
    /// <remarks>
    /// A test seam: each file is classified afresh and no path value is reported, so every <c>Resource()</c> value of a
    /// kind the preview refuses is kept as written. The preview strips through the overload that takes a classification
    /// and a path-value callback.
    /// </remarks>
    /// <param name="document">The converted scene.</param>
    /// <returns>
    /// The stripped scene. A reference to a removed resource, a <c>Resource()</c> value whose file is a script, or an
    /// <c>Object()</c> value whose class is a Script class, in a property value, a node's <c>instance</c>, a bind or an
    /// <c>Object()</c> value's property becomes <c>null</c>; as a typed container's element, key or value type it becomes
    /// <c>Object</c>, the values kept, since the text loader loads every script a type names and a GDScript runs its static
    /// initializer as it loads. A <c>Resource()</c> type becomes <c>Object</c> whatever it names.
    /// </returns>
    internal static TscnDocument WithoutScripts(TscnDocument document) =>
        WithoutScripts(document, new HashSet<string>(StringComparer.Ordinal), XscnPreviewReferences.ClassifyFile, (_, _) => { });

    /// <summary>
    /// The document with no script from its own text, as <see cref="WithoutScripts(TscnDocument)"/> gives it, and no
    /// connection from or to a node that lost its script in a scene the document instances.
    /// </summary>
    /// <param name="document">The converted scene or resource.</param>
    /// <param name="lostScripts">The paths from the root, simplified, of the instanced nodes whose script was removed.</param>
    /// <param name="kindOf">What the preview makes of a resolved path, read for every ext_resource and path value.</param>
    /// <param name="pathValue">Called with each <c>Resource()</c> value's resolved path and kind, the strip's own answer.</param>
    /// <returns>The stripped document.</returns>
    internal static TscnDocument WithoutScripts(
        TscnDocument document,
        IReadOnlySet<string> lostScripts,
        Func<string, PreviewReferenceKind> kindOf,
        Action<string, PreviewReferenceKind> pathValue
    )
    {
        // The text loader loads every ext_resource and sub_resource, and a built-in script runs its static initializer as
        // its source is set (modules/gdscript/gdscript.cpp), so a script must leave the text, not only the nodes.
        var removedExts = new HashSet<string>(
            // An ext is a script by the file it resolves to (XscnPreviewReferences); its declared type, the converter's guess,
            // can only add one.
            document
                .ExtResources.Where(ext => IsScriptClass(ext.Type) || XscnPreviewReferences.Classify(ext, kindOf) == PreviewReferenceKind.Script)
                .Select(ext => ext.Id),
            StringComparer.Ordinal
        );
        var removedSubs = new HashSet<string>(
            document.SubResources.Where(sub => IsScriptClass(sub.Type)).Select(sub => sub.Id),
            StringComparer.Ordinal
        );
        var strippedNodes = new HashSet<string>(lostScripts, StringComparer.Ordinal);
        foreach (TscnNode node in document.Nodes)
        {
            if (AddScriptReferences(node.Properties, removedExts, removedSubs))
            {
                strippedNodes.Add(NodePath(node));
            }
        }

        foreach (TscnSubResource sub in document.SubResources)
        {
            AddScriptReferences(sub.Properties, removedExts, removedSubs);
        }

        AddScriptReferences(document.ResourceProperties, removedExts, removedSubs);
        var removed = new RemovedResources(removedExts, removedSubs, kindOf, pathValue);
        return WithoutUnknownFields(
            document with
            {
                ExtResources = [.. document.ExtResources.Where(ext => !removedExts.Contains(ext.Id))],
                SubResources =
                [
                    .. document
                        .SubResources.Where(sub => !removedSubs.Contains(sub.Id))
                        .Select(sub => sub with { Properties = Stripped(sub.Properties, removed) }),
                ],
                ResourceProperties = Stripped(document.ResourceProperties, removed),
                // The text loader parses a node's instance= as a value too, a Resource() path loading as it is parsed.
                Nodes =
                [
                    .. document.Nodes.Select(node =>
                        node with
                        {
                            Properties = Stripped(node.Properties, removed),
                            Instance = node.Instance is { } instance ? Nulled(instance, removed) : null,
                        }
                    ),
                ],
                // Godot logs an error for each connection whose signal or method went with the script (object.cpp:1529,1550).
                Connections =
                [
                    .. document
                        .Connections.Where(connection =>
                            !strippedNodes.Contains(Simplified(connection.From)) && !strippedNodes.Contains(Simplified(connection.To))
                        )
                        .Select(connection => connection with { Binds = [.. connection.Binds.Select(bind => Nulled(bind, removed))] }),
                ],
            }
        );
    }

    // The document with no tag field the model does not hold, in any section. The text loader parses every tag field as a
    // value, a Resource() path loading as it is parsed (variant_parser.cpp, _parse_tag, with no Resource( hook set by
    // resource_format_text.cpp), and the model holds every field Godot reads.
    private static TscnDocument WithoutUnknownFields(TscnDocument document) =>
        document with
        {
            Header = document.Header with { Extra = [] },
            ExtResources = [.. document.ExtResources.Select(ext => ext with { Extra = [] })],
            SubResources = [.. document.SubResources.Select(sub => sub with { Extra = [] })],
            Nodes = [.. document.Nodes.Select(node => node with { Extra = [] })],
            Connections = [.. document.Connections.Select(connection => connection with { Extra = [] })],
            Editables = [.. document.Editables.Select(editable => editable with { Extra = [] })],
        };

    /// <summary>Whether the type is Script or a class that inherits it, by the addon's metadata.</summary>
    /// <param name="type">A class name.</param>
    /// <returns>True for a Script class.</returns>
    internal static bool IsScriptClass(string type)
    {
        if (string.Equals(type, ScriptClass, StringComparison.Ordinal))
        {
            return true;
        }

        GodotMetadata metadata = XscnMetadata.Value;
        return metadata.FindClass(type) is { } cls && metadata.Inherits(cls, ScriptClass);
    }

    // Adds the resource each script reference names to the removed ones; true when the properties hold a script reference.
    private static bool AddScriptReferences(ImmutableArray<TscnProperty> properties, HashSet<string> removedExts, HashSet<string> removedSubs)
    {
        bool any = false;
        foreach (TscnProperty property in properties.Where(IsScriptReference))
        {
            any = true;
            if (property.Value is GodotResourceRef reference)
            {
                (reference.Kind == GodotRefKind.External ? removedExts : removedSubs).Add(reference.Id);
            }
        }

        return any;
    }

    // A script property that names a script. Any other value, such as the explicit null an instance or an override carries
    // to detach its scene's script (Script="{x:Null}"), attaches nothing and stays.
    private static bool IsScriptReference(TscnProperty property) =>
        string.Equals(property.Name, ScriptProperty, StringComparison.Ordinal) && property.Value is GodotResourceRef or GodotPathRef;

    /// <summary>The paths of the document's nodes whose script the strip removes.</summary>
    /// <param name="document">The converted scene.</param>
    /// <returns>Each path from the root, simplified.</returns>
    internal static IEnumerable<string> ScriptedNodePaths(TscnDocument document) =>
        document.Nodes.Where(node => node.Properties.Any(IsScriptReference)).Select(NodePath);

    /// <summary>Whether the node sets its script to something other than a script, as an instance does to detach its scene's.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True when a <c>script</c> property names no script.</returns>
    internal static bool DetachesScript(TscnNode node) =>
        node.Properties.Any(property => string.Equals(property.Name, ScriptProperty, StringComparison.Ordinal) && !IsScriptReference(property));

    /// <summary>A node's path from the root, as a connection's from and to name it: "." for the root.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The path, simplified.</returns>
    internal static string NodePath(TscnNode node) =>
        node.Parent is null ? "." : Simplified(node.Parent == "." ? node.Name : node.Parent + "/" + node.Name);

    /// <summary>
    /// A relative path simplified as NodePath::simplify does (core/string/node_path.cpp): empty and "." segments are dropped,
    /// a ".." removes itself and the segment before it unless that segment is another "..", when both stay; nothing left is
    /// ".".
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The simplified path.</returns>
    internal static string Simplified(string path)
    {
        var segments = new List<string>();
        foreach (string segment in path.Split('/').Where(segment => segment.Length > 0 && segment != "."))
        {
            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return segments.Count == 0 ? "." : string.Join('/', segments);
    }

    private static ImmutableArray<TscnProperty> Stripped(ImmutableArray<TscnProperty> properties, RemovedResources removed) =>
        [.. properties.Where(property => !IsScriptReference(property)).Select(property => property with { Value = Nulled(property.Value, removed) })];

    // The value with each reference to a removed resource, each Resource() value whose file is a script, and each Object()
    // value whose class is a Script class made null, inside containers and objects too: the text loader loads a
    // Resource() value by its path as it parses it (variant_parser.cpp:1124-1176), inside an Object() value too
    // (:1011-1085), and an Object(GDScript, "script/source": …) compiles its source and runs its static initializer as it
    // is set (modules/gdscript/gdscript.cpp, GDScript::_set and reload).
    private static GodotValue Nulled(GodotValue value, RemovedResources removed) =>
        value switch
        {
            GodotResourceRef reference => removed.Contains(reference) ? GodotNull.Instance : value,
            GodotPathRef reference => removed.RemovesPath(reference) ? GodotNull.Instance : value,
            ObjectValue objectValue when IsScriptClass(objectValue.ClassName) => GodotNull.Instance,
            GodotArray array => new GodotArray([.. array.Items.Select(item => Nulled(item, removed))], Retyped(array.ElementType, removed)),
            GodotDictionary dictionary => new GodotDictionary(
                [.. dictionary.Entries.Select(entry => new GodotEntry(Nulled(entry.Key, removed), Nulled(entry.Value, removed)))],
                Retyped(dictionary.KeyType, removed),
                Retyped(dictionary.ValueType, removed)
            ),
            ObjectValue objectValue => new ObjectValue(
                objectValue.ClassName,
                [.. objectValue.Properties.Select(property => property with { Value = Nulled(property.Value, removed) })]
            ),
            _ => value,
        };

    // A container type that names a removed script becomes Object, which every script's instance is. A Resource() type
    // becomes Object whatever it names, since the text loader loads its file as it parses the type
    // (variant_parser.cpp:1217-1237, 1263-1283, 1354-1371) and types the container only by a script, which Object covers.
    private static GodotTypeRef? Retyped(GodotTypeRef? type, RemovedResources removed) =>
        type?.Script switch
        {
            GodotResourceRef reference when removed.Contains(reference) => ObjectType,
            GodotPathRef => ObjectType,
            _ => type,
        };

    // The ext_resource and sub_resource ids the strip removes, and the preview's answer for a Resource() value's file.
    private sealed record RemovedResources(
        HashSet<string> Exts,
        HashSet<string> Subs,
        Func<string, PreviewReferenceKind> KindOf,
        Action<string, PreviewReferenceKind> PathValue
    )
    {
        public bool Contains(GodotResourceRef reference) => (reference.Kind == GodotRefKind.External ? Exts : Subs).Contains(reference.Id);

        // Whether either file the value may load is a script, each reported; a value that resolves to no path loads
        // nothing and stays.
        public bool RemovesPath(GodotPathRef reference)
        {
            bool script = false;
            foreach (string resolved in XscnPreviewReferences.ResolvedPaths(reference))
            {
                PreviewReferenceKind kind = KindOf(resolved);
                PathValue(resolved, kind);
                script |= kind == PreviewReferenceKind.Script;
            }

            return script;
        }
    }
}
