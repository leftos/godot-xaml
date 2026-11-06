using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>
/// Saves a resource as <c>.xscn</c> or <c>.xres</c>: Godot's own text saver writes it to a uniquely named <c>.tscn</c> or
/// <c>.tres</c> in the addon's per-user temp folder (<see cref="AddonTempFolder"/>), outside the project, so no uid is
/// minted for it; the text is converted forward, keeping the <c>x:Resources</c> keys of the file it replaces, written to a
/// hidden staging file beside the target, and renamed over the target.
/// </summary>
internal static class XscnSaveRoute
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Saves the resource to the path in the canonical layout.</summary>
    /// <param name="resource">The resource; a <see cref="PackedScene"/> for an <c>.xscn</c>.</param>
    /// <param name="path">The <c>.xscn</c> or <c>.xres</c> to write.</param>
    /// <param name="flags">The <see cref="ResourceSaver.SaverFlags"/> the caller passed, handed on to Godot's text saver.</param>
    /// <returns><see cref="Error.Ok"/>, or the error that stopped the save; nothing is written on an error.</returns>
    public static Error Save(Resource resource, string path, uint flags)
    {
        if (MetadataOfPrevious(path) is not { } previousMetadata)
        {
            return Error.InvalidData;
        }

        // The file is written with the running engine's metadata and stamped with its version, as Godot resaves a .tscn in
        // its own version (docs/DECISIONS.md); the previous file's own metadata only parses it.
        GodotMetadata metadata = XscnMetadata.Value;

        bool isScene = resource is PackedScene;
        Error textError = SaveAsText(resource, path, flags, isScene, out string tscnText);
        if (textError != Error.Ok)
        {
            return textError;
        }

        TscnDocument tscn;
        try
        {
            tscn = TscnReader.Read(tscnText);
        }
        catch (TscnParseException exception)
        {
            GD.PushError($"{path}: godot-xaml cannot read the text Godot's text saver wrote: {exception.Message}");
            return Error.InvalidData;
        }

        XscnDocument? previous = Previous(path, isScene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource, previousMetadata);
        var options = new ConversionOptions
        {
            Seed = XscnLoading.Seed,
            ScriptClassOf = GodotProjectLookups.ScriptClassOf,
            Previous = previous is null ? null : new PreviousDocument(previous, previousMetadata),
            OwnedSubResourceIds = OwnedSubResourceIds(resource, path, previous),
        };
        TscnDocument withUids = WithProjectUids(tscn, path, previous);
        ConversionResult<XscnDocument> converted = TscnToXscn.Convert(withUids, metadata, options);
        if (PushIssues(path, converted.Issues))
        {
            return Error.InvalidData;
        }

        string text;
        try
        {
            text = XscnWriter.Write(converted.Document, metadata);
        }
        catch (Exception exception) when (WriterRefusal.Is(exception))
        {
            GD.PushError($"{path}: godot-xaml cannot write the converted document: {exception.Message}");
            return Error.InvalidData;
        }

        Error written = WriteAtomically(path, text);
        if (written == Error.Ok && TakesOverPaths(path, flags))
        {
            TakeOverSubResourcePaths(resource, path, converted);
        }

        return written;
    }

    // The text saver asks for an ext_resource's uid through a callback only the editor registers
    // (resource_format_text.cpp:1884, editor_file_system.cpp:3817), so outside the editor its ext_resources carry none and
    // x:Uids would come out empty; each takes the uid the project knows for its path, else the one the replaced file gave it,
    // since outside the editor the project knows only uid_cache.bin (resource_loader.cpp:1414-1415).
    private static TscnDocument WithProjectUids(TscnDocument tscn, string path, XscnDocument? previous)
    {
        Dictionary<string, string> kept = new(StringComparer.Ordinal);
        foreach (XscnUid row in previous?.Uids ?? [])
        {
            kept.TryAdd(XscnLoading.Localized(path, row.Path), row.Uid);
        }

        return tscn with
        {
            ExtResources = [.. tscn.ExtResources.Select(ext => ext.Uid is null ? ext with { Uid = KnownUid(ext.Path, kept) } : ext)],
        };
    }

    // The same paths the editor's lookup refuses (editor_file_system.cpp:3673), so a user:// copy gains no row in the editor.
    private static string? KnownUid(string path, Dictionary<string, string> kept)
    {
        if (!path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("res://.godot", StringComparison.Ordinal))
        {
            return null;
        }

        long uid = ResourceLoader.GetResourceUid(path);
        return uid == ResourceUid.InvalidId ? kept.GetValueOrDefault(path) : ResourceUid.IdToText(uid);
    }

    // The sub-resources the saved file owns, each id the text was written with to the id the file being replaced loaded it
    // with. Make Local and inspector drag-and-drop give a scene another file's built-in sub-resources, which can carry the
    // same id as one of its own; Godot's text saver then keeps the id on the first of the two it meets and re-mints the
    // other's on the live resource (resource_format_text.cpp, ResourceFormatSaverTextInstance::save), so the live id is the
    // text's and the path still names the id it loaded with. ResourceSaver hands the saver the path unlocalized
    // (resource_saver.cpp, ResourceSaver::save). A load names a sub-resource from the localized path, which simplifies a
    // "res://a/../b" (project_settings.cpp, ProjectSettings::localize_path), but TakeOverSubResourcePaths, like Godot's text
    // saver, names it from the raw one, so either form marks the file's own (SubResourceOwner). Read before
    // TakeOverSubResourcePaths renames them. Another file's save takes over the sub-resources it borrowed from this one, so
    // a path that names no file of its own falls back to the file and id the sub-resource was loaded with
    // (SubResourceOrigins), which no save changes.
    // The map pairs only with the previous file's x:Resources entries, so with none there is nothing to walk for.
    private static Dictionary<string, string> OwnedSubResourceIds(Resource saved, string path, XscnDocument? previous) =>
        previous is null || previous.Resources.IsDefaultOrEmpty
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : OwnedSubResourceIds(saved, path);

    private static Dictionary<string, string> OwnedSubResourceIds(Resource saved, string path)
    {
        string localized = ProjectSettings.LocalizePath(path);
        Dictionary<string, string> owned = new(StringComparer.Ordinal);
        XscnLoadRoute.WalkOwnResources(
            saved,
            IsBuiltIn,
            resource =>
            {
                // The root's path is the file's or empty, never "<file>::<id>", so it is no sub-resource of the file.
                if (SubResourceOwner(resource.ResourcePath, path, localized, out string id) == Owner.ThisFile)
                {
                    owned[resource.ResourceSceneUniqueId] = id;
                }
                else if (
                    resource.GetInstanceId() != saved.GetInstanceId()
                    && SubResourceOrigins.TryGet(resource, out (string File, string Id) origin)
                    && (string.Equals(origin.File, path, StringComparison.Ordinal) || string.Equals(origin.File, localized, StringComparison.Ordinal))
                )
                {
                    owned[resource.ResourceSceneUniqueId] = origin.Id;
                }
            }
        );
        return owned;
    }

    private enum Owner
    {
        // An empty path or a file's own path, not "<file>::<id>".
        None,
        ThisFile,
        OtherFile,
    }

    // Which file a "<file>::<id>" path names: the saved one when its file part is the path the saver was given or its
    // localized form, another file's otherwise. The id is the part after "::", empty unless the owner is ThisFile.
    private static Owner SubResourceOwner(string resourcePath, string path, string localizedPath, out string id)
    {
        id = "";
        int separator = resourcePath.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return Owner.None;
        }

        string file = resourcePath[..separator];
        if (!string.Equals(file, path, StringComparison.Ordinal) && !string.Equals(file, localizedPath, StringComparison.Ordinal))
        {
            return Owner.OtherFile;
        }

        id = resourcePath[(separator + 2)..];
        return Owner.ThisFile;
    }

    private static bool TakesOverPaths(string path, uint flags) =>
        ((ResourceSaver.SaverFlags)flags).HasFlag(ResourceSaver.SaverFlags.ReplaceSubresourcePaths)
        && path.StartsWith("res://", StringComparison.Ordinal);

    // Godot's text saver takes each sub-resource over itself (resource_format_text.cpp:1941-1943), but not on a save outside
    // res:// (:1777-1779), and its save here goes to the temp folder. The ids it assigns (:1927-1934) are not the ones the
    // next load mints for a resource with no id yet; a loaded one keeps the loader's id (:1898-1906). So each sub-resource
    // takes the loader's id for the same sub_resource before its path; one the conversion has no id for is left as it is.
    private static void TakeOverSubResourcePaths(Resource saved, string path, ConversionResult<XscnDocument> converted)
    {
        Dictionary<string, string> loaderIds = LoaderIds(converted);
        XscnLoadRoute.WalkOwnResources(
            saved,
            IsBuiltIn,
            resource =>
            {
                if (resource.GetInstanceId() != saved.GetInstanceId() && loaderIds.TryGetValue(resource.ResourceSceneUniqueId, out string? id))
                {
                    resource.ResourceSceneUniqueId = id;
                    resource.TakeOverPath(path + "::" + id);
                }
            }
        );
    }

    // Godot's id of each sub_resource to the id the next load mints for it: the forward conversion says which resource of
    // the document each Godot id became, and the loader's own conversion of that document gives the resource its id, which
    // depends only on where the resource is used, so the written file loads with the same one.
    private static Dictionary<string, string> LoaderIds(ConversionResult<XscnDocument> converted)
    {
        Dictionary<string, string> ids = new(StringComparer.Ordinal);
        ConversionIds? loader = XscnLoading.ToTscn(converted.Document).Ids;
        if (converted.Ids is not { } godot || loader is null)
        {
            return ids;
        }

        foreach (XscnResource resource in Resources(converted.Document))
        {
            if (godot.SubIdOf(resource) is { } godotId && loader.SubIdOf(resource) is { } loaderId)
            {
                ids.TryAdd(godotId, loaderId);
            }
        }

        return ids;
    }

    // Every resource the document holds: the table's entries and each resource held inline, at any depth.
    private static IEnumerable<XscnResource> Resources(XscnDocument document) =>
        document.Resources.SelectMany(entry => Held(entry).Prepend(entry)).Concat(Held(document.Root));

    private static IEnumerable<XscnResource> Held(XscnElement element)
    {
        ImmutableArray<XscnProperty> properties = element switch
        {
            XscnNodeElement node => node.Properties,
            XscnResource resource => resource.Properties,
            _ => [],
        };
        IEnumerable<XscnResource> inline = properties
            .Select(property => property.Value)
            .OfType<XscnInline>()
            .SelectMany(value => Held(value.Resource).Prepend(value.Resource));
        return element is XscnNodeElement { Children: var children } ? inline.Concat(children.SelectMany(Held)) : inline;
    }

    // After a load a sub-resource's path is "<old file>::<id>", and after Save As that is another file's path; Resource.is_built_in
    // also counts local:// (core/io/resource.h:147).
    private static bool IsBuiltIn(string resourcePath) =>
        resourcePath.Length == 0
        || resourcePath.Contains("::", StringComparison.Ordinal)
        || resourcePath.StartsWith("local://", StringComparison.Ordinal);

    // Godot's text saver writes the resource to a temp file; its text comes back and the file is deleted.
    private static Error SaveAsText(Resource resource, string path, uint flags, bool isScene, out string text)
    {
        text = "";
        string tempPath = Path.Combine(AddonTempFolder.Get(), $"{Guid.NewGuid():N}.{(isScene ? "tscn" : "tres")}").Replace('\\', '/');
        // Relative paths would be computed against the temp file, which shares no folder with the project, so they would
        // come out absolute.
        ResourceSaver.SaverFlags textFlags = (ResourceSaver.SaverFlags)flags & ~ResourceSaver.SaverFlags.RelativePaths;
        try
        {
            Error error = ResourceSaver.Save(resource, tempPath, textFlags);
            if (error != Error.Ok)
            {
                GD.PushError($"{path}: godot-xaml: Godot's text saver failed: {error}.");
                return error;
            }

            text = File.ReadAllText(tempPath);
            return Error.Ok;
        }
        finally
        {
            DeleteOrWarn(path, tempPath);
        }
    }

    // The metadata the x:Godot marker of the file being replaced names, read from its text whether or not the rest parses,
    // so the file is parsed with the typing it was written with (its x:Resources keys and comments are kept); the running
    // engine's for no file or no marker. The save writes with the running engine's metadata either way. A file whose marker
    // the addon does not carry cannot be parsed with its own typing, so such a file is refused: null after the error.
    private static GodotMetadata? MetadataOfPrevious(string path)
    {
        string? marker = FileAccess.FileExists(path) ? XscnMarker.Read(FileAccess.GetFileAsString(path)) : null;
        if (!XscnMetadata.Carries(marker))
        {
            GD.PushError(XscnMetadata.NotWritten(path, marker!));
            return null;
        }

        return XscnMetadata.For(marker, path);
    }

    // The file being replaced, parsed with the metadata its marker names, so the conversion keeps its keys; null when there
    // is none or it does not parse.
    private static XscnDocument? Previous(string path, XscnDocumentKind kind, GodotMetadata metadata)
    {
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        XscnParseResult result = XscnParser.Parse(FileAccess.GetFileAsString(path), kind, metadata);
        if (result.HasErrors)
        {
            GD.PushWarning($"{path}: godot-xaml: the file being replaced does not parse; its x:Resources keys and comments are not kept.");
            return null;
        }

        return result.Document;
    }

    /// <summary>
    /// Writes the text to a hidden staging file beside the target, on its volume and wherever the target is writable, then
    /// replaces the target in one rename, so a failed write never leaves a half-written file at the path.
    /// </summary>
    /// <param name="path">The <c>res://</c> file to write.</param>
    /// <param name="text">Its new text, written as UTF-8 without a byte order mark.</param>
    /// <returns><see cref="Error.Ok"/>, or <see cref="Error.FileCantWrite"/> with the target left as it was.</returns>
    internal static Error WriteAtomically(string path, string text)
    {
        string target = ProjectSettings.GlobalizePath(path);
        string stagingPath = Path.Combine(Path.GetDirectoryName(target) ?? "", $".{Guid.NewGuid():N}.{path.GetExtension()}.tmp");
        try
        {
            File.WriteAllText(stagingPath, text, Utf8NoBom);
            File.Move(stagingPath, target, overwrite: true);
            return Error.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"{path}: godot-xaml cannot write the file: {exception.Message}");
            DeleteOrWarn(path, stagingPath);
            return Error.FileCantWrite;
        }
    }

    // A cleanup that fails is a warning: it must not replace the save's own result.
    private static void DeleteOrWarn(string path, string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"{path}: godot-xaml cannot delete the temporary file {file}: {exception.Message}");
        }
    }

    /// <summary>Pushes each conversion issue to Godot's log, and tells whether any was an error.</summary>
    private static bool PushIssues(string path, ImmutableArray<ConversionIssue> issues)
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
            else
            {
                GD.PushWarning(message);
            }
        }

        return anyError;
    }
}
