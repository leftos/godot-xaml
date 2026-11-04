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
/// Strips a preview's document and every scene file it references, at any depth: each scene is read from its file,
/// stripped as the previewed text is (<see cref="XscnPreviewScene.WithoutScripts(TscnDocument, IReadOnlySet{string})"/>),
/// and written to the addon's temp folder, and the ext_resource that names it points at that copy. Godot would otherwise
/// load a referenced scene from disk with its scripts. Disposing deletes the copies. Main thread only: it calls Godot.
/// </summary>
internal sealed class XscnPreviewInstances : IDisposable
{
    private readonly string folder = AddonTempFolder.Get();

    // Each scene's res:// path to its copy, written before the scenes it references are read, so a cycle points at it.
    private readonly Dictionary<string, string> copies = new(StringComparer.Ordinal);

    // Each copied scene's res:// path to the paths, from its root, of its nodes that lost their script.
    private readonly Dictionary<string, HashSet<string>> lostScripts = new(StringComparer.Ordinal);

    private readonly List<ConversionIssue> issues = [];

    /// <summary>Each referenced scene that could not be read or stripped, as an error; the preview then loads nothing.</summary>
    internal IReadOnlyList<ConversionIssue> Issues => issues;

    /// <summary>
    /// The document stripped of its scripts, each ext_resource that resolves to a scene pointed at that scene's stripped copy
    /// with no uid, and no connection from or to an instanced node that lost its script.
    /// </summary>
    /// <param name="document">A converted scene whose ext_resource paths are resource paths.</param>
    /// <returns>The document to load; when <see cref="Issues"/> is not empty, one that must not be loaded.</returns>
    internal TscnDocument Stripped(TscnDocument document) => Stripped(document, out _);

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
        return XscnPreviewScene.WithoutScripts(redirected, instanced);
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

    // A text scene is pointed at its stripped copy, the uid dropped since a registered uid wins over the path in the text
    // loader (resource_format_text.cpp:491-507); a scene that cannot be stripped is an issue. Scripts are left to the strip.
    private TscnExtResource Redirected(TscnExtResource ext)
    {
        string resolved = XscnPreviewReferences.ResolvedPath(ext);
        PreviewReferenceKind kind = XscnPreviewReferences.Classify(ext);
        if (kind == PreviewReferenceKind.TextScene)
        {
            return StrippedCopy(resolved) is { } copy ? ext with { Path = copy, Uid = null } : ext;
        }

        if (Refusal(kind, resolved) is { } message)
        {
            Failed(resolved, message);
        }

        return ext;
    }

    // Why a referenced file of this kind cannot be previewed with scripts off, or null when it can.
    private static string? Refusal(PreviewReferenceKind kind, string resolved) =>
        kind switch
        {
            PreviewReferenceKind.BinaryScene => $"{resolved}: a binary scene cannot be previewed with scripts off",
            PreviewReferenceKind.OtherScene => $"{resolved} is not a text scene",
            PreviewReferenceKind.UnknownBinary => $"{resolved}: the binary resource's class cannot be read, so it may hold a scene",
            PreviewReferenceKind.ImportedScene => $"{resolved}: an imported scene cannot be previewed with scripts off; allow scripts to preview it",
            _ => null,
        };

    // The stripped copy's absolute path, written once per scene; null, with an issue, when the scene cannot be read.
    private string? StrippedCopy(string resPath)
    {
        if (copies.TryGetValue(resPath, out string? existing))
        {
            return existing;
        }

        string copy = Path.Combine(folder, $"{Guid.NewGuid():N}.tscn").Replace('\\', '/');
        copies[resPath] = copy;
        TscnDocument? document = Read(resPath);
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

    // The text scene converted to the text loader's form, or null after adding the issue that says why not.
    private TscnDocument? Read(string resPath)
    {
        bool isXscn = XscnLoading.IsScene(resPath);

        // Godot's FileAccess reads a file a mounted pack holds, as the addon's loader does.
        string text = FileAccess.GetFileAsString(resPath);
        Error openError = FileAccess.GetOpenError();
        if (openError != Error.Ok)
        {
            return Failed(resPath, $"{resPath}: the scene cannot be read: {openError}");
        }

        return isXscn ? FromXscn(resPath, text) : FromTscn(resPath, text);
    }

    private TscnDocument? FromXscn(string resPath, string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, XscnMetadata.Value);
        if (parsed.Diagnostics.FirstOrDefault(d => d.Severity == XscnSeverity.Error) is { } parseError)
        {
            return Failed(resPath, $"{resPath}: the scene does not parse: {parseError.Code}: {parseError.Message}");
        }

        ConversionResult<TscnDocument> result = XscnLoading.ToTscn(parsed.Document, resPath);
        if (result.Issues.FirstOrDefault(issue => issue.Severity == ConversionSeverity.Error) is { } conversionError)
        {
            return Failed(resPath, $"{resPath}: the scene does not convert: {conversionError.Code}: {conversionError.Message}");
        }

        return result.Document;
    }

    private TscnDocument? FromTscn(string resPath, string text)
    {
        try
        {
            return TscnReader.Read(text);
        }
        catch (TscnParseException exception)
        {
            return Failed(resPath, $"{resPath}: the scene does not parse: {exception.Message}");
        }
    }

    private TscnDocument? Failed(string resPath, string message)
    {
        issues.Add(new ConversionIssue(ConversionCodes.InstanceNotStripped, ConversionSeverity.Error, message, resPath));
        return null;
    }
}
