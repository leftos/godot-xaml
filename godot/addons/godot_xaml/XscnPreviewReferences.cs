using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>What a preview with scripts off makes of a file the text references.</summary>
internal enum PreviewReferenceKind
{
    /// <summary>Loaded as it is: neither a script nor a scene.</summary>
    Kept,

    /// <summary>A script, removed with every reference to it.</summary>
    Script,

    /// <summary>A text scene (<c>.tscn</c> or <c>.xscn</c>), loaded from a stripped copy.</summary>
    TextScene,

    /// <summary>A binary scene (<c>.scn</c>, or a <c>.res</c> holding a PackedScene), which cannot be stripped.</summary>
    BinaryScene,

    /// <summary>Another file that holds a scene, which cannot be stripped.</summary>
    OtherScene,

    /// <summary>A binary resource whose class cannot be read, which cannot be shown to hold no scene.</summary>
    UnknownBinary,

    /// <summary>A scene the editor imports from a model (<c>.glb</c>, <c>.fbx</c>, …), which is not stripped.</summary>
    ImportedScene,
}

/// <summary>
/// Classifies an ext_resource by the file it resolves to, not by its declared type, which is the converter's guess: the
/// file the text loader loads is the one its registered uid names, and the text sets that uid through <c>x:Uids</c>.
/// Main thread only: it calls Godot.
/// </summary>
internal static class XscnPreviewReferences
{
    private const string UidScheme = "uid://";

    private const string BinaryExtension = "res";

    private const string ScriptClass = "Script";

    private const string PackedSceneClass = "PackedScene";

    // The source formats Godot's scene importers claim (modules/gltf, modules/fbx, editor/import: Collada, OBJ); the editor
    // imports each to a scene whose scripts the import settings choose.
    private static readonly string[] ImportedSceneExtensions = ["glb", "gltf", "fbx", "blend", "dae", "obj"];

    /// <summary>
    /// The path the text loader loads an ext_resource from: its registered uid's, else its path, a uid:// path resolved. A
    /// uid wins over the path when it is registered (resource_format_text.cpp:491-507).
    /// </summary>
    /// <param name="ext">The ext_resource, its path a resource path.</param>
    /// <returns>The resolved path.</returns>
    internal static string ResolvedPath(TscnExtResource ext)
    {
        if (ext.Uid is { } uid && RegisteredPath(uid) is { } byUid)
        {
            return byUid;
        }

        return ext.Path.StartsWith(UidScheme, StringComparison.Ordinal) ? RegisteredPath(ext.Path) ?? ext.Path : ext.Path;
    }

    /// <summary>What the preview makes of the file an ext_resource resolves to.</summary>
    /// <param name="ext">The ext_resource, its path a resource path.</param>
    /// <returns>The kind; a declared scene type only turns a file that would be kept into one that cannot be stripped.</returns>
    internal static PreviewReferenceKind Classify(TscnExtResource ext)
    {
        PreviewReferenceKind kind = ClassifyFile(ResolvedPath(ext));
        // A text resource holding a scene has no class to read outside the editor, so a declared scene fails closed.
        return kind == PreviewReferenceKind.Kept && Inherits(ext.Type, PackedSceneClass) ? PreviewReferenceKind.OtherScene : kind;
    }

    /// <summary>What the preview makes of a file, by its extension, then by its class.</summary>
    /// <param name="path">The resolved path.</param>
    /// <returns>The kind.</returns>
    internal static PreviewReferenceKind ClassifyFile(string path)
    {
        string extension = path.GetExtension();
        if (ByExtension(extension) is { } kind)
        {
            return kind;
        }

        // A missing file holds nothing to strip; Godot reports it as missing when the text loads.
        return FileAccess.FileExists(path) ? ByClass(path, IsAny(extension, BinaryExtension)) : PreviewReferenceKind.Kept;
    }

    private static PreviewReferenceKind? ByExtension(string extension) =>
        ScriptExtensions().Contains(extension) ? PreviewReferenceKind.Script
        : IsAny(extension, "tscn", XscnLoading.SceneExtension) ? PreviewReferenceKind.TextScene
        : IsAny(extension, "scn") ? PreviewReferenceKind.BinaryScene
        : IsAny(extension, ImportedSceneExtensions) ? PreviewReferenceKind.ImportedScene
        : null;

    // The class the editor records for the file, else, for a binary resource, the one its header names.
    private static PreviewReferenceKind ByClass(string path, bool binary)
    {
        string? cls = GodotProjectLookups.Instance.ClassOf(path) ?? (binary ? BinaryResourceType(path) : null);
        return cls switch
        {
            null => binary ? PreviewReferenceKind.UnknownBinary : PreviewReferenceKind.Kept,
            _ when Inherits(cls, PackedSceneClass) => FileAccess.FileExists(path + ".import") ? PreviewReferenceKind.ImportedScene
            : binary ? PreviewReferenceKind.BinaryScene
            : PreviewReferenceKind.OtherScene,
            _ when Inherits(cls, ScriptClass) => PreviewReferenceKind.Script,
            _ => PreviewReferenceKind.Kept,
        };
    }

    private static bool IsAny(string extension, params string[] candidates) =>
        candidates.Any(candidate => candidate.Equals(extension, StringComparison.OrdinalIgnoreCase));

    private static string? RegisteredPath(string uidText)
    {
        long id = ResourceUid.TextToId(uidText);
        return id != ResourceUid.InvalidId && ResourceUid.HasId(id) ? ResourceUid.GetIdPath(id) : null;
    }

    // The extensions only a script loader claims. Every resource loader also claims its own extensions for any resource
    // class, Script included (resource_format_text.cpp:1478-1492 pushes "tres" for any type but PackedScene), so the
    // extensions claimed for Resource are taken out: otherwise every .tres would read as a script.
    private static HashSet<string> ScriptExtensions()
    {
        var extensions = new HashSet<string>(ResourceLoader.GetRecognizedExtensionsForType(ScriptClass), StringComparer.OrdinalIgnoreCase);
        extensions.ExceptWith(ResourceLoader.GetRecognizedExtensionsForType("Resource"));
        return extensions;
    }

    private static bool Inherits(string type, string baseClass)
    {
        if (string.Equals(type, baseClass, StringComparison.Ordinal))
        {
            return true;
        }

        GodotMetadata metadata = XscnMetadata.Value;
        return metadata.FindClass(type) is { } cls && metadata.Inherits(cls, baseClass);
    }

    // The class a binary resource's header names, read as ResourceLoaderBinary::recognize does
    // (core/io/resource_format_binary.cpp:1056-1093, get_unicode_string :864-874): "RSRC", big_endian, use_real64,
    // ver_major, ver_minor, ver_format as 32-bit integers in the file's byte order, then the type as a 32-bit length and
    // that many UTF-8 bytes. A compressed file ("RSCC") or any other is not read here: null, so it fails closed.
    private static string? BinaryResourceType(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null || file.GetLength() < 28 || Encoding.ASCII.GetString(file.GetBuffer(4)) != "RSRC")
        {
            return null;
        }

        file.BigEndian = file.Get32() != 0;
        file.Get32(); // use_real64
        file.Get32(); // ver_major
        file.Get32(); // ver_minor
        file.Get32(); // ver_format
        uint length = file.Get32();
        if (length == 0 || length > file.GetLength() - file.GetPosition())
        {
            return null;
        }

        string type = Encoding.UTF8.GetString(file.GetBuffer(length)).TrimEnd('\0');
        return type.Length > 0 ? type : null;
    }
}
