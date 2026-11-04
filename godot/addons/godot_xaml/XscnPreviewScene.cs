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
/// or Godot could not load or instantiate the converted text (its reason is then in Godot's log).
/// </param>
/// <param name="Diagnostics">The parse's diagnostics, never pushed to Godot's log.</param>
/// <param name="Issues">The conversion's issues, never pushed to Godot's log; empty when the text did not parse.</param>
internal sealed record PreviewScene(Node? Root, ImmutableArray<XscnDiagnostic> Diagnostics, ImmutableArray<ConversionIssue> Issues);

/// <summary>
/// Turns <c>.xscn</c> text, saved or not, into an instantiated scene for the preview server: parsed, converted, loaded
/// through Godot's text loader with the cache ignored, and instantiated. The text is written only to the load route's own
/// temp file, never to the document's path. Main thread only: it calls Godot.
/// </summary>
internal static class XscnPreviewScene
{
    /// <summary>The project setting that lets a request keep the scene's scripts; off when unset.</summary>
    internal const string AllowScriptsSetting = "godot_xaml/preview_server/allow_scripts";

    private const string ScriptProperty = "script";

    // The base of every script class; outside the editor the conversion types a script's ext_resource with it.
    private const string ScriptClass = "Script";

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
            return new PreviewScene(null, parsed.Diagnostics, []);
        }

        ConversionResult<TscnDocument> result = XscnLoading.ToTscn(parsed.Document, resPath);
        if (result.Issues.Any(issue => issue.Severity == ConversionSeverity.Error))
        {
            return new PreviewScene(null, parsed.Diagnostics, result.Issues);
        }

        // The text loader reads the temp file, so each path is made a resource path against the document's, as a load does.
        TscnDocument converted = result.Document with
        {
            ExtResources = [.. result.Document.ExtResources.Select(ext => ext with { Path = XscnLoading.Localized(resPath, ext.Path) })],
        };
        if (!(scriptsRequested && ScriptsAllowed()))
        {
            converted = WithoutScripts(converted);
        }

        return new PreviewScene(LoadAndInstantiate(resPath, converted), parsed.Diagnostics, result.Issues);
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
    /// stays), no Script-class sub-resource or ext_resource, and no connection from or to a node whose script was removed.
    /// Every other property stays: Godot sets each stored node property with <c>node->set</c> and is silent on one the class
    /// lacks (packed_scene.cpp:372-383), so a script's exported values stay where the class has them. Instanced scenes keep
    /// their own scripts.
    /// </summary>
    /// <param name="document">The converted scene.</param>
    /// <returns>
    /// The stripped scene. A reference to a removed resource in a property value or a bind becomes <c>null</c>; one as a typed
    /// container's element type is left, and the load then fails.
    /// </returns>
    internal static TscnDocument WithoutScripts(TscnDocument document)
    {
        // The text loader loads every ext_resource and sub_resource, and a built-in script runs its static initializer as
        // its source is set (modules/gdscript/gdscript.cpp), so a script must leave the text, not only the nodes.
        var removedExts = new HashSet<string>(
            document.ExtResources.Where(ext => IsScriptClass(ext.Type)).Select(ext => ext.Id),
            StringComparer.Ordinal
        );
        var removedSubs = new HashSet<string>(
            document.SubResources.Where(sub => IsScriptClass(sub.Type)).Select(sub => sub.Id),
            StringComparer.Ordinal
        );
        var strippedNodes = new HashSet<string>(StringComparer.Ordinal);
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

        var removed = new RemovedResources(removedExts, removedSubs);
        return document with
        {
            ExtResources = [.. document.ExtResources.Where(ext => !removedExts.Contains(ext.Id))],
            SubResources =
            [
                .. document
                    .SubResources.Where(sub => !removedSubs.Contains(sub.Id))
                    .Select(sub => sub with { Properties = Stripped(sub.Properties, removed) }),
            ],
            ResourceProperties = Stripped(document.ResourceProperties, removed),
            Nodes = [.. document.Nodes.Select(node => node with { Properties = Stripped(node.Properties, removed) })],
            // Godot logs an error for each connection whose signal or method went with the script (object.cpp:1529,1550).
            Connections =
            [
                .. document
                    .Connections.Where(connection =>
                        !strippedNodes.Contains(Simplified(connection.From)) && !strippedNodes.Contains(Simplified(connection.To))
                    )
                    .Select(connection => connection with { Binds = [.. connection.Binds.Select(bind => Nulled(bind, removed))] }),
            ],
        };
    }

    private static bool IsScriptClass(string type)
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

    // A node's path from the root, as a connection's from and to name it: "." for the root.
    private static string NodePath(TscnNode node) =>
        node.Parent is null ? "." : Simplified(node.Parent == "." ? node.Name : node.Parent + "/" + node.Name);

    // A relative path simplified as NodePath::simplify does (core/string/node_path.cpp): empty and "." segments are dropped,
    // a ".." removes itself and the segment before it unless that segment is another "..", when both stay; nothing left is
    // ".".
    private static string Simplified(string path)
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

    // The value with each reference to a removed resource made null, inside containers and objects too.
    private static GodotValue Nulled(GodotValue value, RemovedResources removed) =>
        value switch
        {
            GodotResourceRef reference => removed.Contains(reference) ? GodotNull.Instance : value,
            GodotArray array => new GodotArray([.. array.Items.Select(item => Nulled(item, removed))], array.ElementType),
            GodotDictionary dictionary => new GodotDictionary(
                [.. dictionary.Entries.Select(entry => new GodotEntry(Nulled(entry.Key, removed), Nulled(entry.Value, removed)))],
                dictionary.KeyType,
                dictionary.ValueType
            ),
            ObjectValue objectValue => new ObjectValue(
                objectValue.ClassName,
                [.. objectValue.Properties.Select(property => property with { Value = Nulled(property.Value, removed) })]
            ),
            _ => value,
        };

    // The ext_resource and sub_resource ids the strip removes.
    private sealed record RemovedResources(HashSet<string> Exts, HashSet<string> Subs)
    {
        public bool Contains(GodotResourceRef reference) => (reference.Kind == GodotRefKind.External ? Exts : Subs).Contains(reference.Id);
    }
}
