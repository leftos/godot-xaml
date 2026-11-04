using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>
/// Strips a preview's document and every text scene and text resource file it references, at any depth: each is read from
/// its file, stripped as the previewed text is (<see cref="XscnPreviewScene.WithoutScripts(TscnDocument)"/>), and written to
/// the addon's temp folder, and the ext_resource that names it points at that copy. Godot would otherwise load a referenced
/// file from disk with its scripts. A <c>Resource()</c> value is loaded by its path, never a copy: one that names a script
/// is made null, and one that names a file the preview would copy or refuse is refused. A referenced resource whose header
/// type is a Script class is a script, never copied, and a script ext_resource is never copied. Disposing deletes the copies. Main
/// thread only: it calls Godot.
/// </summary>
internal sealed class XscnPreviewInstances : IDisposable
{
    private static readonly string AllowScriptsHint = $"turn on {XscnPreviewScene.AllowScriptsSetting} to preview it";

    private readonly string folder = AddonTempFolder.Get();

    // Each scene's or resource's res:// path to its copy, written before the files it references are read, so a cycle
    // points at it.
    private readonly Dictionary<string, string> copies = new(StringComparer.Ordinal);

    // Each resolved path's classification, made once per preview: the redirect, the refusal and the strip read the same
    // answer.
    private readonly Dictionary<string, PreviewClassification> classifications = new(StringComparer.Ordinal);

    // Each copied scene's res:// path to the paths, from its root, of its nodes that lost their script.
    private readonly Dictionary<string, HashSet<string>> lostScripts = new(StringComparer.Ordinal);

    private readonly List<ConversionIssue> issues = [];

    /// <summary>Each referenced scene that could not be read or stripped, as an error; the preview then loads nothing.</summary>
    internal IReadOnlyList<ConversionIssue> Issues => issues;

    /// <summary>
    /// The document stripped of its scripts, each ext_resource that resolves to a scene pointed at that scene's stripped copy
    /// with no uid, and no connection from or to an instanced node that lost its script. A project that declares a global
    /// class whose native base is <c>ResourceFormatLoader</c> gets the document back as it was, with one issue per class:
    /// nothing is read, classified or copied.
    /// </summary>
    /// <param name="document">A converted scene whose ext_resource paths are resource paths.</param>
    /// <returns>The document to load; when <see cref="Issues"/> is not empty, one that must not be loaded.</returns>
    internal TscnDocument Stripped(TscnDocument document)
    {
        // Godot registers a global class whose native base is ResourceFormatLoader as a loader, and a built-in loader that
        // fails on a file hands that file on to them (resource_loader.cpp, add_custom_loaders), which runs the class's
        // script: with scripts off such a project gets no preview.
        IReadOnlyList<(string Class, string Path)> loaderClasses = XscnPreviewReferences.ProjectLoaderClasses();
        foreach ((string className, string path) in loaderClasses)
        {
            Failed(path, LoaderClassRefusal(className));
        }

        return loaderClasses.Count > 0 ? document : Stripped(document, out _);
    }

    // Why a project's loader class leaves nothing to preview.
    private static string LoaderClassRefusal(string className) =>
        $"{className} is a ResourceFormatLoader class, which Godot registers as a loader that a failed load hands files to; {AllowScriptsHint}";

    /// <summary>Deletes every copy written; one that cannot be deleted is a warning, and the others are still deleted.</summary>
    public void Dispose()
    {
        foreach (string copy in copies.Values)
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"godot-xaml: could not remove the preview's temp scene {copy}: {exception.Message}");
            }
        }

        copies.Clear();
    }

    private TscnDocument Stripped(TscnDocument document, out HashSet<string> lost)
    {
        TscnDocument redirected = document with { ExtResources = [.. document.ExtResources.Select(Redirected)] };
        HashSet<string> instanced = LostUnderInstances(document);
        lost = [.. instanced, .. XscnPreviewScene.ScriptedNodePaths(document)];
        return XscnPreviewScene.WithoutScripts(redirected, instanced, KindOf, RefusePathValue);
    }

    private PreviewReferenceKind KindOf(string resolved) => Classified(resolved).Kind;

    private PreviewClassification Classified(string resolved)
    {
        if (!classifications.TryGetValue(resolved, out PreviewClassification? classification))
        {
            classification = XscnPreviewReferences.Classification(resolved);
            classifications[resolved] = classification;
        }

        return classification;
    }

    // A Resource() value is loaded by its path as the text parses it (variant_parser.cpp:1124-1176), so it cannot point at
    // a copy: a script is made null by the strip, a kept file stays, and any other kind is refused, with its own reason
    // when it has one.
    private void RefusePathValue(string resolved, PreviewReferenceKind kind)
    {
        if (kind is not (PreviewReferenceKind.Script or PreviewReferenceKind.Kept))
        {
            Failed(
                resolved,
                Refusal(kind, resolved, declaredType: null)
                    ?? $"{resolved} is referenced by a Resource() value, which the preview cannot redirect to a stripped copy"
            );
        }
    }

    // The paths, from the document's root, of the nodes in its instanced scenes that lost their script. An instance that
    // detaches its scene's script itself keeps its connections, as it would with scripts allowed.
    private HashSet<string> LostUnderInstances(TscnDocument document)
    {
        var lost = new HashSet<string>(StringComparer.Ordinal);
        foreach (TscnNode node in document.Nodes)
        {
            if (
                node.Instance is not GodotResourceRef { Kind: GodotRefKind.External } reference
                || document.ExtResources.FirstOrDefault(ext => ext.Id == reference.Id) is not { } ext
                || !lostScripts.TryGetValue(XscnPreviewReferences.ResolvedPath(ext), out HashSet<string>? inside)
            )
            {
                continue;
            }

            string at = XscnPreviewScene.NodePath(node);
            bool detached = XscnPreviewScene.DetachesScript(node);
            lost.UnionWith(inside.Where(path => !(detached && path == ".")).Select(path => Joined(at, path)));
        }

        return lost;
    }

    private static string Joined(string at, string path) =>
        at == "." ? path
        : path == "." ? at
        : XscnPreviewScene.Simplified(at + "/" + path);

    // A text scene or text resource is pointed at its stripped copy, the uid dropped since a registered uid wins over the
    // path in the text loader (resource_format_text.cpp:491-507); a file that cannot be stripped is an issue. Scripts are
    // left to the strip.
    private TscnExtResource Redirected(TscnExtResource ext)
    {
        // A script by its declared type is the strip's to remove, never copied.
        if (XscnPreviewScene.IsScriptClass(ext.Type))
        {
            return ext;
        }

        string resolved = XscnPreviewReferences.ResolvedPath(ext);
        PreviewReferenceKind kind = XscnPreviewReferences.Classify(ext, KindOf);
        if (kind is PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource)
        {
            return StrippedCopy(resolved, kind) is { } copy ? ext with { Path = copy, Uid = null } : ext;
        }

        if (Refusal(kind, resolved, ext.Type) is { } message)
        {
            Failed(resolved, message);
        }

        return ext;
    }

    // Why a referenced file of this kind cannot be previewed with scripts off, or null when it can; the file's own
    // classification names a remap's target and a sidecar's reason, and the declared type names the class.
    private string? Refusal(PreviewReferenceKind kind, string resolved, string? declaredType) =>
        kind switch
        {
            PreviewReferenceKind.Remapped or PreviewReferenceKind.UnsafeSidecar => Refusal(Classified(resolved), resolved),
            PreviewReferenceKind.UnknownClass => $"{resolved} is declared as {declaredType}, a class neither Godot nor the addon's metadata "
                + $"knows, so it may be a script; {AllowScriptsHint}",
            _ when Classified(resolved) is { ImportTarget: not null } imported && imported.Kind == kind => Refusal(imported, resolved),
            _ => Refusal(new PreviewClassification(kind), resolved),
        };

    // A refusal of a file the importer loads names that file, as a remap target's does.
    private static string? Refusal(PreviewClassification classification, string resolved) =>
        classification is { ImportTarget: { } target, ImportTargetClassification: { } inner }
            ? $"{resolved} is imported to {target}: {Refusal(inner, target)}"
            : KindRefusal(classification, resolved);

    private static string? KindRefusal(PreviewClassification classification, string resolved) =>
        classification.Kind switch
        {
            PreviewReferenceKind.BinaryScene => $"{resolved}: a binary scene cannot be previewed with scripts off",
            PreviewReferenceKind.OtherScene => $"{resolved} is not a text scene",
            PreviewReferenceKind.UnknownBinary => $"{resolved}: the binary resource's class cannot be read, so it may hold a scene",
            PreviewReferenceKind.ImportedScene => $"{resolved}: an imported scene cannot be previewed with scripts off; allow scripts to preview it",
            PreviewReferenceKind.BinaryResource => $"{resolved} is a binary resource, which the preview cannot strip of scripts; "
                + $"save it as .tres or turn on {XscnPreviewScene.AllowScriptsSetting}",
            PreviewReferenceKind.NativeLibrary => $"{resolved} is a GDExtension library, which loading opens as native code; {AllowScriptsHint}",
            PreviewReferenceKind.UnknownExtension =>
                $"{resolved} has an extension no Godot loader claims, so a project loader script could run on load; {AllowScriptsHint}",
            PreviewReferenceKind.UnsafeSidecar => $"{classification.Reason}; {AllowScriptsHint}",
            PreviewReferenceKind.Remapped when classification is { RemapTarget: { } target, RemapTargetClassification: { } inner } =>
                $"{resolved} is remapped to {target}, which could carry a script: {RemapTargetRefusal(inner, target)}",
            _ => null,
        };

    // Why the file a remap may load cannot be loaded in the original's place.
    private static string RemapTargetRefusal(PreviewClassification target, string path) =>
        target.Kind switch
        {
            PreviewReferenceKind.Script => $"{path} is a script; {AllowScriptsHint}",
            PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource =>
                $"{path} is a text file the preview cannot strip in place; {AllowScriptsHint}",
            _ => Refusal(target, path) ?? $"{path} cannot be shown to hold no script; {AllowScriptsHint}",
        };

    // The stripped copy's absolute path, written once per file; null, with an issue, when the file cannot be read, and null
    // with no issue when the file is a resource whose type is a Script class, which then reads as a script.
    private string? StrippedCopy(string resPath, PreviewReferenceKind kind)
    {
        if (copies.TryGetValue(resPath, out string? existing))
        {
            return existing;
        }

        // The copy is named by what it holds, since the ext_resource's type hint picks the loader by the extension: .tres
        // for any type but PackedScene, .tscn for a PackedScene (resource_format_text.cpp:1478-1492).
        TscnDocument? document = Read(resPath, kind);

        // A resource that is itself a script (a GDScript saved as .tres) is a script: no copy, and the strip removes every
        // reference to it, since the path's one answer now says so.
        if (document is { Kind: TscnKind.Resource, Header.Type: { } type } && XscnPreviewScene.IsScriptClass(type))
        {
            classifications[resPath] = new PreviewClassification(PreviewReferenceKind.Script);
            return null;
        }

        string extension = document?.Kind == TscnKind.Resource ? "tres" : "tscn";
        string copy = Path.Combine(folder, $"{Guid.NewGuid():N}.{extension}").Replace('\\', '/');
        copies[resPath] = copy;
        if (document is null)
        {
            return null;
        }

        // Its paths are made resource paths against its own path first, as a load of it does, since the copy is read from
        // the temp folder (resource_format_text.cpp:509-512).
        TscnDocument localized = document with
        {
            Header = document.Header with { Uid = null },
            ExtResources = [.. document.ExtResources.Select(ext => ext with { Path = XscnLoading.Localized(resPath, ext.Path) })],
        };
        File.WriteAllText(copy, TscnWriter.Write(Stripped(localized, out HashSet<string> lost)));
        lostScripts[resPath] = lost;
        return copy;
    }

    // The text scene or resource converted to the text loader's form, or null after adding the issue that says why not.
    private TscnDocument? Read(string resPath, PreviewReferenceKind kind)
    {
        string noun = kind == PreviewReferenceKind.TextResource ? "resource" : "scene";

        // Godot's FileAccess reads a file a mounted pack holds, as the addon's loader does.
        string text = FileAccess.GetFileAsString(resPath);
        Error openError = FileAccess.GetOpenError();
        if (openError != Error.Ok)
        {
            return Failed(resPath, $"{resPath}: the {noun} cannot be read: {openError}");
        }

        return XscnLoading.IsScene(resPath) ? FromXscn(resPath, text, XscnDocumentKind.Scene, noun)
            : XscnLoading.IsResource(resPath) ? FromXscn(resPath, text, XscnDocumentKind.Resource, noun)
            : FromTscn(resPath, text, noun);
    }

    private TscnDocument? FromXscn(string resPath, string text, XscnDocumentKind documentKind, string noun)
    {
        XscnParseResult parsed = XscnParser.Parse(text, documentKind, XscnMetadata.Value);
        if (parsed.Diagnostics.FirstOrDefault(d => d.Severity == XscnSeverity.Error) is { } parseError)
        {
            return Failed(resPath, $"{resPath}: the {noun} does not parse: {parseError.Code}: {parseError.Message}");
        }

        ConversionResult<TscnDocument> result = XscnLoading.ToTscn(parsed.Document, resPath);
        if (result.Issues.FirstOrDefault(issue => issue.Severity == ConversionSeverity.Error) is { } conversionError)
        {
            return Failed(resPath, $"{resPath}: the {noun} does not convert: {conversionError.Code}: {conversionError.Message}");
        }

        return result.Document;
    }

    private TscnDocument? FromTscn(string resPath, string text, string noun)
    {
        try
        {
            return TscnReader.Read(text);
        }
        catch (TscnParseException exception)
        {
            return Failed(resPath, $"{resPath}: the {noun} does not parse: {exception.Message}");
        }
    }

    private TscnDocument? Failed(string resPath, string message)
    {
        issues.Add(new ConversionIssue(ConversionCodes.InstanceNotStripped, ConversionSeverity.Error, message, resPath));
        return null;
    }
}
