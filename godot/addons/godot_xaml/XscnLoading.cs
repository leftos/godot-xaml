using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>
/// The work the <c>.xscn</c> and <c>.xres</c> loaders share: reading, parsing and converting a file, loading it through
/// Godot's text loader, listing its dependencies and rewriting the paths it refers to.
/// </summary>
internal static class XscnLoading
{
    /// <summary>The extension of a scene.</summary>
    public const string SceneExtension = "xscn";

    /// <summary>The extension of a resource.</summary>
    public const string ResourceExtension = "xres";

    // The id seed the loader and the saver share: a save pairs a sub_resource with its entry by the ids this seed mints.
    internal const ulong Seed = 0;

    private static readonly ConversionOptions Options = new()
    {
        Seed = Seed,
        ExtResourceTypes = GodotProjectLookups.Instance,
        ScriptClassOf = GodotProjectLookups.ScriptClassOf,
        Uid = null,
    };

    private static int loadCallCount;

    /// <summary>How many times Godot has called either loader's <c>_Load</c> since the assembly loaded.</summary>
    internal static int LoadCallCount => Volatile.Read(ref loadCallCount);

    /// <summary>Whether the path names an <c>.xscn</c>.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>True for an <c>.xscn</c>, in any letter case.</returns>
    public static bool IsScene(string path) => path.GetExtension().Equals(SceneExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the path names an <c>.xres</c>.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>True for an <c>.xres</c>, in any letter case.</returns>
    public static bool IsResource(string path) => path.GetExtension().Equals(ResourceExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Loads an <c>.xscn</c> or <c>.xres</c>: parsed, converted, then loaded through Godot's text loader.</summary>
    /// <param name="path">The file.</param>
    /// <param name="originalPath">The path asked for before any remap, which relative references resolve against.</param>
    /// <param name="cacheMode">The <see cref="ResourceFormatLoader.CacheMode"/> Godot passed.</param>
    /// <returns>The loaded resource, or the <see cref="Error"/> that stopped the load, as an int.</returns>
    public static Variant Load(string path, string originalPath, int cacheMode)
    {
        Interlocked.Increment(ref loadCallCount);
        try
        {
            return LetGoNextFrame(LoadConverted(path, originalPath, cacheMode));
        }
        // Godot reads an exception escaping a virtual as a nil return: the load would fail with no reason given.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            GD.PushError($"{path}: godot-xaml: the load failed: {exception}");
            return (int)Error.Failed;
        }
    }

    // Godot's generated dispatch of _Load copies the returned Variant and never disposes it (ScriptMethodsGenerator.cs
    // :436-459), so its reference would keep the loaded resource in the cache until a finalizer runs. It is disposed once,
    // when the calling thread's message queue next flushes: on the main thread a frame later, on a loader worker thread
    // right after _load returns (core/io/resource_loader.cpp:524-544). Disposed now, it would be empty before Godot copies it.
    private static Variant LetGoNextFrame(Variant result)
    {
        Variant ours = result;
        Callable.From(() => ours.Dispose()).CallDeferred();
        return result;
    }

    /// <summary>The file's dependencies, one entry per external resource in the converted text, in the text loader's format.</summary>
    /// <param name="path">The file.</param>
    /// <param name="addTypes">Whether each entry carries the dependency's class.</param>
    /// <returns>The entries; none when the file does not read, parse or convert.</returns>
    public static string[] Dependencies(string path, bool addTypes)
    {
        try
        {
            // The text loader's dependency pass is silent on a valid file, so only errors are pushed.
            // Listing needs no property order, so the scan thread parses no instanced scene.
            ReadAndConvert(path, sceneKey: null, pushWarnings: false, out TscnDocument? converted);
            return converted is null ? [] : [.. converted.ExtResources.Select(ext => DependencyEntry(path, ext, addTypes))];
        }
        // Godot reads an exception escaping a virtual as a nil return, which is an empty list.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            GD.PushError($"{path}: godot-xaml: listing the dependencies failed: {exception}");
            return [];
        }
    }

    /// <summary>Rewrites the paths the file refers to from a map of old path to new path, and writes the file when any changed.</summary>
    /// <param name="path">The file.</param>
    /// <param name="renames">Old <c>res://</c> path to new <c>res://</c> path, as Godot passes it.</param>
    /// <returns>
    /// <see cref="Error.Ok"/>; <see cref="Error.FileCorrupt"/> when the file does not parse; the read or write error;
    /// <see cref="Error.Failed"/> on an unexpected exception. Nothing is written on an error.
    /// </returns>
    public static Error RenameDependencies(string path, Godot.Collections.Dictionary renames)
    {
        try
        {
            return Rename(path, renames);
        }
        // Godot reads an exception escaping a virtual as a nil return, which is Error.OK: the rename would read as done
        // while the file is unchanged.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            GD.PushError($"{path}: godot-xaml: renaming the dependencies failed: {exception}");
            return Error.Failed;
        }
    }

    /// <summary>The class an <c>.xres</c> file's root tag names, read without parsing the rest; <c>Resource</c> when it names none.</summary>
    /// <param name="path">The <c>.xres</c>.</param>
    /// <returns>The class name.</returns>
    public static string XresRootClass(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
        try
        {
            using FileStream stream = File.OpenRead(ProjectSettings.GlobalizePath(path));
            using var reader = XmlReader.Create(stream, settings);
            bool named =
                reader.MoveToContent() == XmlNodeType.Element
                && reader.Prefix.Length == 0
                && ClassDB.ClassExists(reader.LocalName)
                && ClassDB.IsParentClass(reader.LocalName, "Resource");
            return named ? reader.LocalName : "Resource";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return "Resource";
        }
    }

    /// <summary>The script an <c>.xres</c> file's root tag names, read without parsing the rest, as a <c>res://</c> path.</summary>
    /// <param name="path">The <c>.xres</c>.</param>
    /// <returns>The script's <c>res://</c> path, or null when the root names none.</returns>
    internal static string? XresRootScript(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
        try
        {
            using FileStream stream = File.OpenRead(ProjectSettings.GlobalizePath(path));
            using var reader = XmlReader.Create(stream, settings);
            string? script = reader.MoveToContent() == XmlNodeType.Element ? reader.GetAttribute("Script") : null;
            return script is null || script.Length == 0 ? null : Localized(path, script);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>Converts a parsed document to the text loader's form, minting its sub-resource ids as every load does.</summary>
    /// <param name="document">The parsed <c>.xscn</c> or <c>.xres</c>.</param>
    /// <returns>The converted document and the conversion's issues.</returns>
    internal static ConversionResult<TscnDocument> ToTscn(XscnDocument document) => XscnToTscn.Convert(document, XscnMetadata.Value, Options);

    /// <summary>
    /// Converts a parsed document as <see cref="ToTscn(XscnDocument)"/> does, reading the scenes it instances so their
    /// nodes' properties come back in Godot's order.
    /// </summary>
    /// <param name="document">The parsed <c>.xscn</c> or <c>.xres</c>.</param>
    /// <param name="path">The file's <c>res://</c> path, which its relative references are resolved against.</param>
    /// <returns>The converted document and the conversion's issues.</returns>
    internal static ConversionResult<TscnDocument> ToTscn(XscnDocument document, string path) =>
        XscnToTscn.Convert(document, XscnMetadata.Value, Options with { SceneFiles = GodotSceneFiles.Instance, SourceKey = path });

    private static Variant LoadConverted(string path, string originalPath, int cacheMode)
    {
        // The text loader resolves a relative path against the file it reads (resource_format_text.cpp:509-512), and the
        // converted text is read from the temp folder, so each path is made a resource path here, against the path asked for
        // before any remap, as the text loader's own load does (:1445). The instanced scenes are read against it too.
        string asked = originalPath.Length > 0 ? originalPath : path;
        Error error = ReadAndConvert(path, asked, pushWarnings: true, out TscnDocument? converted);
        if (converted is null)
        {
            return (int)error;
        }

        converted = converted with { ExtResources = [.. converted.ExtResources.Select(ext => ext with { Path = Localized(asked, ext.Path) })] };
        bool isScene = IsScene(path);
        string typeHint = isScene ? "PackedScene" : converted.Header.Type ?? "Resource";
        return XscnLoadRoute.Load(path, TscnWriter.Write(converted), isScene, typeHint, (ResourceFormatLoader.CacheMode)cacheMode);
    }

    // The entry the text loader gives an ext_resource (resource_format_text.cpp:946-977):
    //     if (next_tag.fields.has("uid")) {
    //         ResourceUID::ID uid = ResourceUID::get_singleton()->text_to_id(uidt);
    //         if (uid != ResourceUID::INVALID_ID) {
    //             fallback_path = path; // Used by Dependency Editor, in case uid path fails.
    //             path = ResourceUID::get_singleton()->id_to_text(uid);
    //             using_uid = true;
    //     if (!using_uid && !path.contains("://") && path.is_relative_path()) {
    //         path = ProjectSettings::get_singleton()->localize_path(local_path.get_base_dir().path_join(path));
    //     if (p_add_types) { path += "::" + type; }
    //     if (!fallback_path.is_empty()) {
    //         if (!p_add_types) { path += "::"; // Ensure that path comes third, even if there is no type.
    //         path += "::" + fallback_path;
    private static string DependencyEntry(string path, TscnExtResource ext, bool addTypes)
    {
        long uid = ext.Uid is null ? ResourceUid.InvalidId : ResourceUid.TextToId(ext.Uid);
        bool usingUid = uid != ResourceUid.InvalidId;
        string entry = usingUid ? ResourceUid.IdToText(uid) : Localized(path, ext.Path);
        if (addTypes)
        {
            entry += "::" + ext.Type;
        }

        if (usingUid)
        {
            entry += (addTypes ? "" : "::") + "::" + ext.Path;
        }

        return entry;
    }

    // A path relative to the file, made a resource path as the text loader does.
    internal static string Localized(string path, string dependency) =>
        !dependency.Contains("://", StringComparison.Ordinal) && dependency.IsRelativePath()
            ? ProjectSettings.LocalizePath(path.GetBaseDir().PathJoin(dependency))
            : dependency;

    private static Error Rename(string path, Godot.Collections.Dictionary renames)
    {
        Error readError = ReadAndParse(path, pushWarnings: false, out XscnDocument? document);
        if (document is null)
        {
            return readError == Error.ParseError ? Error.FileCorrupt : readError;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<Variant, Variant> pair in renames)
        {
            map[pair.Key.AsString()] = pair.Value.AsString();
        }

        var full = new Dictionary<string, string>(map, StringComparer.Ordinal);
        var targets = map.Values.Distinct(StringComparer.Ordinal).ToDictionary(target => target, StringComparer.Ordinal);
        AddRelativeSpellings(path, document, full, targets);

        // The rewriter warns about each map key it finds as a substring of unparsed text; a relative key such as
        // "theme.xres" would match inside unrelated text, so the warnings come from the absolute map alone and the
        // document from the full one.
        foreach (string warning in XscnPathRewriter.Rewrite(document, map).Warnings)
        {
            GD.PushWarning($"{path}: godot-xaml: {warning}");
        }

        XscnDocument rewritten = XscnPathRewriter.Rewrite(document, full).Document;
        string text = XscnWriter.Write(XscnRenameUids.Retarget(rewritten, targets), XscnMetadata.Value);
        return text == XscnWriter.Write(document, XscnMetadata.Value) ? Error.Ok : XscnSaveRoute.WriteAtomically(path, text);
    }

    // The text loader resolves a path that does not start with res:// against the file's folder, maps it, and makes it
    // relative again (resource_format_text.cpp:1060-1073):
    //     if (!path.begins_with("res://")) { path = base_path.path_join(path).simplify_path(); relative = true; }
    //     if (p_map.has(path)) { path = p_map[path]; }
    //     if (relative) { path = base_path.path_to_file(path); }
    // So each relative path the document holds whose resolved path is renamed maps to the new path relative to the folder.
    private static void AddRelativeSpellings(string path, XscnDocument document, Dictionary<string, string> full, Dictionary<string, string> targets)
    {
        string folder = path.GetBaseDir();
        foreach (string relative in RelativePaths(document))
        {
            if (full.TryGetValue(folder.PathJoin(relative).SimplifyPath(), out string? newPath))
            {
                string spelled = RelativeSpelling(folder, newPath);
                full.TryAdd(relative, spelled);
                targets.TryAdd(spelled, newPath);
            }
        }
    }

    // Every path with no "://" the document holds: x:Uids paths, file properties and Resource() references.
    private static HashSet<string> RelativePaths(XscnDocument document)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? candidate)
        {
            if (candidate is not null && !candidate.Contains("://", StringComparison.Ordinal))
            {
                found.Add(candidate);
            }
        }

        foreach (XscnUid uid in document.Uids)
        {
            Add(uid.Path);
        }

        var walk = new XscnReferenceWalk(
            reference =>
            {
                Add(reference.Path);
                return reference;
            },
            file =>
            {
                Add(file.Path);
                return file;
            }
        );
        walk.Map(document);
        if (document.Root is XscnNodeElement root)
        {
            AddScenePaths(root, Add);
        }

        return found;
    }

    // Instances and an inherited root name their scene in the node's type; the engine rewrites them as ext_resources.
    private static void AddScenePaths(XscnNodeElement element, Action<string?> add)
    {
        if (element is XscnNode { Type: XscnSceneType scene })
        {
            add(scene.Path);
        }

        foreach (XscnNodeElement child in element.Children)
        {
            AddScenePaths(child, add);
        }
    }

    /// <summary>How a rename writes a new path relative to the renamed file's folder.</summary>
    /// <param name="folder">The renamed file's folder.</param>
    /// <param name="newPath">The new <c>res://</c> path.</param>
    /// <returns>The path as <c>path_to_file</c> gives it, or the absolute path when that does not resolve back to it.</returns>
    internal static string RelativeSpelling(string folder, string newPath)
    {
        string relative = GodotPathText.PathToFile(folder, newPath);
        // The engine writes path_to_file's result as is, but from a file in the project root it climbs above res:// ("../sub/x"),
        // and simplify_path keeps the ".." (ustring.cpp:4204), so the reference would not load; the absolute path does.
        return folder.PathJoin(relative).SimplifyPath() == newPath ? relative : newPath;
    }

    // The converted document, or null with the error after pushing the reason to Godot's log; warnings only when asked.
    // With a scene key, the instanced scenes are read against it for their classes; null reads none.
    private static Error ReadAndConvert(string path, string? sceneKey, bool pushWarnings, out TscnDocument? converted)
    {
        converted = null;
        Error error = ReadAndParse(path, pushWarnings, out XscnDocument? document);
        if (document is null)
        {
            return error;
        }

        ConversionResult<TscnDocument> result = sceneKey is null ? ToTscn(document) : ToTscn(document, sceneKey);
        if (PushIssues(path, result.Issues, pushWarnings))
        {
            return Error.ParseError;
        }

        converted = result.Document;
        return Error.Ok;
    }

    // The parsed document, or null with the error after pushing the reason to Godot's log; warnings only when asked.
    private static Error ReadAndParse(string path, bool pushWarnings, out XscnDocument? document)
    {
        document = null;
        string text = FileAccess.GetFileAsString(path);
        Error openError = FileAccess.GetOpenError();
        if (openError != Error.Ok)
        {
            GD.PushError($"{path}: godot-xaml cannot read the file: {openError}.");
            return openError;
        }

        XscnParseResult result = XscnParser.Parse(text, IsScene(path) ? XscnDocumentKind.Scene : XscnDocumentKind.Resource, XscnMetadata.Value);
        PushDiagnostics(path, text, result.Diagnostics, pushWarnings);
        if (result.HasErrors)
        {
            return Error.ParseError;
        }

        document = result.Document;
        return Error.Ok;
    }

    // Each diagnostic goes to Godot's log with its own severity: an error always, a warning only when asked.
    private static void PushDiagnostics(string path, string text, ImmutableArray<XscnDiagnostic> diagnostics, bool pushWarnings)
    {
        XscnDiagnostic[] pushed = [.. diagnostics.Where(d => (d.Severity == XscnSeverity.Error || pushWarnings) && !KnownToEngine(d, text))];
        if (pushed.Length == 0)
        {
            return;
        }

        var lines = new LineMap(text);
        foreach (XscnDiagnostic diagnostic in pushed)
        {
            LinePosition position = lines.OneBased(diagnostic.Span.Start);
            string message = $"{path}:{position.Line}:{position.Column}: {diagnostic.Code}: {diagnostic.Message}";
            if (diagnostic.Severity == XscnSeverity.Error)
            {
                GD.PushError(message);
            }
            else
            {
                GD.PushWarning(message);
            }
        }
    }

    // A GDExtension class can be registered in this engine and missing from the bundled metadata, and the file still loads
    // through it, so a GX0107 naming a class the running engine has is not a warning. The diagnostic's span is the element's tag.
    private static bool KnownToEngine(XscnDiagnostic diagnostic, string text) =>
        diagnostic.Code == XscnCodes.UnknownClass && ClassDB.ClassExists(text.Substring(diagnostic.Span.Start, diagnostic.Span.Length));

    /// <summary>Pushes each conversion error, and each warning when asked, to Godot's log, and tells whether any was an error.</summary>
    private static bool PushIssues(string path, ImmutableArray<ConversionIssue> issues, bool pushWarnings)
    {
        bool anyError = false;
        foreach (ConversionIssue issue in issues)
        {
            string message = $"{path}: {issue.Code}: {issue.Message} ({issue.Location})";
            if (issue.Severity == ConversionSeverity.Error)
            {
                anyError = true;
                GD.PushError(message);
            }
            else if (pushWarnings)
            {
                GD.PushWarning(message);
            }
        }

        return anyError;
    }
}
