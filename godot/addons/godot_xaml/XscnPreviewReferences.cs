using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>What a preview with scripts off makes of a file the text references.</summary>
internal enum PreviewReferenceKind
{
    /// <summary>Loaded as it is: a file one of Godot's own loaders claims, shown to hold no script, scene or native library.</summary>
    Kept,

    /// <summary>A script, removed with every reference to it.</summary>
    Script,

    /// <summary>A text scene (<c>.tscn</c>, <c>.xscn</c>, or a text file whose first tag is <c>[gd_scene</c>), loaded from a stripped copy.</summary>
    TextScene,

    /// <summary>A binary scene (<c>.scn</c>, or any <c>RSRC</c> file holding a PackedScene), which cannot be stripped.</summary>
    BinaryScene,

    /// <summary>Another file that holds a scene, which cannot be stripped.</summary>
    OtherScene,

    /// <summary>A binary resource whose class cannot be read, which cannot be shown to hold no scene.</summary>
    UnknownBinary,

    /// <summary>A scene the editor imports from a model (<c>.glb</c>, <c>.fbx</c>, …), which is not stripped.</summary>
    ImportedScene,

    /// <summary>A text resource (<c>.xres</c>, or any text file whose first tag is <c>[gd_resource</c>), loaded from a stripped copy.</summary>
    TextResource,

    /// <summary>A binary resource that is not a scene, which cannot be stripped and may hold a built-in or external script.</summary>
    BinaryResource,

    /// <summary>A <c>.gdextension</c> file, whose load opens a native library (gdextension_resource_format.cpp).</summary>
    NativeLibrary,

    /// <summary>A file whose extension none of Godot's own loaders claims, which a project loader script may claim.</summary>
    UnknownExtension,

    /// <summary>A file Godot loads another file in place of, one of which could carry a script.</summary>
    Remapped,

    /// <summary>A <c>.import</c> or <c>.remap</c> sidecar Godot would load a script or an unproven file through.</summary>
    UnsafeSidecar,

    /// <summary>A reference declared as a class neither Godot nor the addon's metadata knows, which may be a script.</summary>
    UnknownClass,
}

/// <summary>What a preview with scripts off makes of a file, and why, where the kind alone cannot say.</summary>
/// <param name="Kind">The kind.</param>
internal sealed record PreviewClassification(PreviewReferenceKind Kind)
{
    /// <summary>For <see cref="PreviewReferenceKind.Remapped"/>: the file Godot may load in place of this one that could carry a script.</summary>
    internal string? RemapTarget { get; init; }

    /// <summary>For <see cref="PreviewReferenceKind.Remapped"/>: what the preview makes of <see cref="RemapTarget"/>.</summary>
    internal PreviewClassification? RemapTargetClassification { get; init; }

    /// <summary>For a refusal the importer's view gives: the file the importer loads that the refusal is about.</summary>
    internal string? ImportTarget { get; init; }

    /// <summary>For a refusal the importer's view gives: what the preview makes of <see cref="ImportTarget"/>.</summary>
    internal PreviewClassification? ImportTargetClassification { get; init; }

    /// <summary>For <see cref="PreviewReferenceKind.UnsafeSidecar"/>: the refusal message.</summary>
    internal string? Reason { get; init; }
}

/// <summary>
/// Classifies an ext_resource by the file it resolves to, not by its declared type, which is the converter's guess: the
/// file the text loader loads is the one its registered uid names, and the text sets that uid through <c>x:Uids</c>. A
/// file's kind comes from its content where Godot's loaders read it by content, from the files a remap may load in its
/// place, and from its <c>.import</c> sidecar; its extension, the editor's class and the declared type can only add a
/// refusal. The extensions a script loader claims come from a fixed table, never from the live loaders, so classifying a
/// file runs no project loader's code. It also names the project's global classes Godot registers as resource loaders, which
/// a preview with scripts off refuses. Main thread only: it calls Godot.
/// </summary>
internal static partial class XscnPreviewReferences
{
    private const string UidScheme = "uid://";

    private const string ScriptClass = "Script";

    private const string LoaderClass = "ResourceFormatLoader";

    private const string PackedSceneClass = "PackedScene";

    private const string GDExtensionClass = "GDExtension";

    private const string ObjectClass = "Object";

    private const string TranslationRemapsSetting = "internationalization/locale/translation_remaps";

    // Read this far into a file for its magic and its first tag.
    private const int HeadLength = 4096;

    // The source formats Godot's scene importers claim (modules/gltf, modules/fbx, editor/import: Collada, OBJ); the editor
    // imports each to a scene whose scripts the import settings choose.
    private static readonly string[] ImportedSceneExtensions = ["glb", "gltf", "fbx", "blend", "dae", "obj"];

    // The resource base extensions, every one claimed by ResourceFormatLoaderBinary (core/io/resource_format_binary.cpp:1216-1225,
    // ClassDB::get_resource_base_extensions), each registered by RES_BASE_EXTENSION or add_resource_base_extension in
    // Godot 4.7.2: res (core/io/resource.cpp), scn (packed_scene.h), theme, stylebox, phymat, multimesh, meshtex, mesh,
    // material, tex (image_texture.h, drawable_texture_2d.h), fontdata, dpitex, curvetex, sample (audio_stream_wav.h),
    // shape (3d/shape_3d.h), atlastex, anim, meshlib (3d/mesh_library.h), all under scene/resources; lmbake
    // (scene/3d/lightmap_gi.h), occ (scene/3d/occluder_instance_3d.h), translation (core/string/translation.h),
    // oggvorbisstr (modules/vorbis), mp3str (modules/mp3), repl (modules/multiplayer).
    private static readonly string[] BinaryExtensions =
    [
        "res",
        "scn",
        "theme",
        "stylebox",
        "phymat",
        "multimesh",
        "meshtex",
        "mesh",
        "material",
        "tex",
        "fontdata",
        "dpitex",
        "curvetex",
        "sample",
        "shape",
        "atlastex",
        "anim",
        "meshlib",
        "lmbake",
        "occ",
        "translation",
        "oggvorbisstr",
        "mp3str",
        "repl",
    ];

    // The extensions Godot's own loaders claim besides the binary loader's, as Godot 4.7.2 registers them with
    // ResourceLoader::add_resource_format_loader (core/register_core_types.cpp, scene/register_scene_types.cpp,
    // modules/*/register_types.cpp): po, mo (TranslationLoaderPO, core/io/translation_loader_po.cpp); image
    // (ResourceFormatLoaderImage, core/io/image_resource_format.cpp); crt, key, pub (ResourceFormatLoaderCrypto,
    // core/crypto/crypto_resource_format.cpp); json (ResourceFormatLoaderJSON, core/io/json_resource_format.cpp); ctex,
    // ctexarray, ccube, ccubearray, ctex3d (the compressed texture loaders, scene/resources/compressed_texture_resource_format.cpp);
    // tscn, tres (ResourceFormatLoaderText, scene/resources/resource_format_text.cpp); gdshader, gdshaderinc (the shader and
    // shader include loaders, scene/resources/shader_resource_format.cpp, shader_include_resource_format.cpp); ogv
    // (modules/theora); dds (modules/dds); ktx, ktx2 (modules/ktx); gd, gdc (modules/gdscript); cs (modules/mono); and the
    // addon's own xscn and xres. The importer claims any file with a .import sidecar, and gdextension is refused by name.
    // Godot asks loaders in order and keeps the first load that succeeds (ResourceLoader::_load, core/io/resource_loader.cpp:
    // "for (int i = 0; i < loader_count; i++) { if (!loader[i]->recognize_path(p_path, p_type_hint)) continue; ... if
    // (res.is_valid()) break; }"), and a project's script loader is appended after them
    // (ResourceLoader::add_custom_resource_format_loader: "ResourceLoader::add_resource_format_loader(crl);", whose
    // p_at_front defaults to false and appends "loader[loader_count++] = p_format_loader;"), so one of these loads a file it
    // claims before a script loader is asked. A loader recognizes a file by its extension for the request's type hint
    // (ResourceFormatLoader::recognize_path, resource_loader.cpp:62-83, and handles_type :135-139): the shader loader only
    // for Shader, TranslationLoaderPO only for Translation, Theora's only for VideoStream. A load that fails goes on to the
    // next loader that recognizes the file (_load, :284-293), the importer among them, so a file with a .import sidecar is
    // classified both ways. Any other extension is refused.
    private static readonly string[] OwnLoaderExtensions =
    [
        "po",
        "mo",
        "image",
        "crt",
        "key",
        "pub",
        "json",
        "ctex",
        "ctexarray",
        "ccube",
        "ccubearray",
        "ctex3d",
        "tscn",
        "tres",
        "gdshader",
        "gdshaderinc",
        "ogv",
        "dds",
        "ktx",
        "ktx2",
        "gd",
        "gdc",
        "cs",
        XscnLoading.SceneExtension,
        XscnLoading.ResourceExtension,
    ];

    // Where the importer writes the files it loads in a source file's place (ResourceFormatImporter, the .import sidecar's
    // path=); a binary resource there is the importer's output, not a project file.
    private const string ImporterOutputFolder = "res://.godot/imported/";

    /// <summary>
    /// A test seam in front of <c>ClassDB</c>: whether class A inherits class B, or null when it does not know A. Set only
    /// by the checks, which put it back.
    /// </summary>
    internal static Func<string, string, bool?>? ClassProbe { get; set; }

    /// <summary>
    /// A test seam in front of the project's class list: each class with its script path and its direct base, or null to
    /// read the class cache. The walk and the exemption run either way. Set only by the checks, which put it back.
    /// </summary>
    internal static Func<IReadOnlyList<(string Class, string Path, string Base)>>? GlobalClassProbe { get; set; }

    /// <summary>
    /// The project's global classes whose native base is <c>ResourceFormatLoader</c>, each with its script's path: Godot
    /// registers every one of them as a resource loader (<c>ResourceLoader::add_custom_loaders</c>, resource_loader.cpp),
    /// and a built-in loader that fails on a file hands that file on to them, which runs the class's script.
    /// </summary>
    /// <remarks>
    /// The class cache is read fresh each time (<c>ProjectSettings.GetGlobalClassList</c>, project_settings.cpp, which runs
    /// no script), and each class is walked to its native base through the list's own classes, as
    /// <c>ScriptServer::get_global_class_native_base</c> walks it (script_language.cpp). Every class counts, abstract or not,
    /// tool or not, as Godot registers them all; one whose bases form a cycle is refused too, since the walk proves nothing
    /// about its base. The addon's own loaders are exempt, since every project with the addon holds them and would otherwise
    /// refuse every preview.
    /// </remarks>
    /// <returns>The classes, in the list's order; empty when the project declares none or has no class cache yet.</returns>
    internal static IReadOnlyList<(string Class, string Path)> ProjectLoaderClasses()
    {
        List<string> names = [];
        var baseOfClass = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathOfClass = new Dictionary<string, string>(StringComparer.Ordinal);
        if (GlobalClassProbe is { } probe)
        {
            foreach ((string className, string path, string baseName) in probe())
            {
                names.Add(className);
                baseOfClass[className] = baseName;
                pathOfClass[className] = path;
            }
        }
        else
        {
            ReadClassCache(names, baseOfClass, pathOfClass);
        }

        List<(string Class, string Path)> loaderClasses = [];
        foreach (string name in names)
        {
            string path = pathOfClass[name];
            string? nativeBase = NativeBase(baseOfClass, name);
            // A walk that repeats a class never reaches a native base, so nothing proves the class is not a loader: it is
            // refused like one.
            if ((nativeBase is null || nativeBase == LoaderClass) && !IsOwnLoader(name, path))
            {
                loaderClasses.Add((name, path));
            }
        }

        return loaderClasses;
    }

    // The project's class cache, each class's name, script path and direct base, in the list's order.
    private static void ReadClassCache(List<string> names, Dictionary<string, string> baseOfClass, Dictionary<string, string> pathOfClass)
    {
        // The list, each item, each class's Dictionary, its keys and its values are all owned by the caller, so each is
        // disposed as it is read: one left to its finalizer can be destroyed after the engine has shut down, which crashes
        // the process. The typed array wrapper has no Dispose of its own, but casting it to Godot.Collections.Array returns
        // the untyped Array it wraps (modules/mono/glue/GodotSharp/GodotSharp/Core/Array.cs:1205-1208).
        using Variant classKey = "class";
        using Variant pathKey = "path";
        using Variant baseKey = "base";
        using var list = (Godot.Collections.Array)ProjectSettings.GetGlobalClassList();
        foreach (Variant item in list)
        {
            using (item)
            {
                using Godot.Collections.Dictionary entry = item.AsGodotDictionary();
                using Variant className = entry[classKey];
                using Variant path = entry[pathKey];
                using Variant baseName = entry[baseKey];
                string name = className.AsString();
                names.Add(name);
                baseOfClass[name] = baseName.AsString();
                pathOfClass[name] = path.AsString();
            }
        }
    }

    // The engine class a global class derives from, as ScriptServer::get_global_class_native_base walks it: the first base
    // name the list does not hold; null when the name is not a global class, or its bases repeat.
    private static string? NativeBase(Dictionary<string, string> baseOfClass, string className)
    {
        if (!baseOfClass.TryGetValue(className, out string? current))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { className };
        while (baseOfClass.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // The addon's own two loaders, which every project with the addon registers: exempt by class name and script path both,
    // so a project's class that merely shares a name is still refused.
    private static bool IsOwnLoader(string className, string path) =>
        (className is nameof(XscnFormatLoader) or nameof(XresFormatLoader)) && XscnExportPlugin.LoaderScripts.Contains(path);

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

    /// <summary>
    /// The paths the text loader may load a <c>Resource()</c> value from, as it parses the value: its registered uid's, and
    /// its path made a resource path, which it loads when the uid's file does not load (variant_parser.cpp:1158-1171,
    /// resource_loader.cpp:689-698).
    /// </summary>
    /// <param name="reference">The value.</param>
    /// <returns>
    /// The uid's path first, then the value's own, each once; empty when the value names no path and an unregistered uid,
    /// which loads nothing.
    /// </returns>
    internal static IReadOnlyList<string> ResolvedPaths(GodotPathRef reference)
    {
        string?[] candidates = [UidPath(reference), reference.Path is { } path && !IsUid(path) ? ProjectSettings.LocalizePath(path) : null];
        return [.. candidates.OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    // The path a Resource() value's registered uid names, from its uid or a uid:// path; null when it has none registered.
    private static string? UidPath(GodotPathRef reference) =>
        (reference.Uid ?? (IsUid(reference.Path) ? reference.Path : null)) is { } uid ? RegisteredPath(uid) : null;

    /// <summary>What the preview makes of the file an ext_resource resolves to, its file classified by the given function.</summary>
    /// <param name="ext">The ext_resource, its path a resource path.</param>
    /// <param name="classifyFile">
    /// What the preview makes of a resolved path: <see cref="ClassifyFile"/>, or a preview's answer for the path.
    /// </param>
    /// <returns>
    /// The kind; the declared type only adds a refusal: a GDExtension, a class neither Godot nor the metadata knows, or a
    /// scene declared for a file that would be kept.
    /// </returns>
    internal static PreviewReferenceKind Classify(TscnExtResource ext, Func<string, PreviewReferenceKind> classifyFile) =>
        ByDeclaredType(classifyFile(ResolvedPath(ext)), ext.Type);

    private static PreviewReferenceKind ByDeclaredType(PreviewReferenceKind kind, string? type)
    {
        // A script's references are removed, and a refused file stays refused, whatever the type says.
        if (
            string.IsNullOrEmpty(type)
            || kind is not (PreviewReferenceKind.Kept or PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource)
        )
        {
            return kind;
        }

        return Inherits(type, ObjectClass) is null ? PreviewReferenceKind.UnknownClass
            : Inherits(type, GDExtensionClass) == true ? PreviewReferenceKind.NativeLibrary
            // A text resource holding a scene has no class to read outside the editor, so a declared scene fails closed.
            : kind == PreviewReferenceKind.Kept && Inherits(type, PackedSceneClass) == true ? PreviewReferenceKind.OtherScene
            : kind;
    }

    /// <summary>What the preview makes of a file: <see cref="Classification"/>'s kind.</summary>
    /// <param name="path">The resolved path.</param>
    /// <returns>The kind.</returns>
    internal static PreviewReferenceKind ClassifyFile(string path) => Classification(path).Kind;

    /// <summary>
    /// What the preview makes of a file, in this order: its extension where that alone decides (a script, a scene the
    /// preview copies or refuses, a <c>.gdextension</c>); the files a remap may load in its place; its <c>.import</c>
    /// sidecar; its content (<c>RSRC</c> or <c>RSCC</c> magic, a text file's first tag); whether one of Godot's own
    /// loaders claims its extension; and the editor's class, which can only add a refusal.
    /// </summary>
    /// <param name="path">The resolved path.</param>
    /// <returns>The classification.</returns>
    internal static PreviewClassification Classification(string path) => Classified(path, followRemaps: true);

    private static PreviewClassification Classified(string path, bool followRemaps)
    {
        string extension = path.GetExtension();
        if (ByExtension(extension) is { } byExtension)
        {
            return new PreviewClassification(byExtension);
        }

        PreviewClassification own = Unremapped(path, extension);
        return followRemaps ? WithRemaps(path, own) : own;
    }

    private static PreviewReferenceKind? ByExtension(string extension) =>
        ScriptExtensions.Contains(extension) ? PreviewReferenceKind.Script
        : IsAny(extension, "tscn", XscnLoading.SceneExtension) ? PreviewReferenceKind.TextScene
        : IsAny(extension, XscnLoading.ResourceExtension) ? PreviewReferenceKind.TextResource
        : IsAny(extension, "scn") ? PreviewReferenceKind.BinaryScene
        : IsAny(extension, ImportedSceneExtensions) ? PreviewReferenceKind.ImportedScene
        // Loading one opens its native library (gdextension_resource_format.cpp:36-53, gdextension_manager.cpp).
        : IsAny(extension, "gdextension") ? PreviewReferenceKind.NativeLibrary
        : null;

    // The file's own kind, no remap followed. A file whose extension none of Godot's own loaders claims is the importer's
    // when its .import sidecar names a type, else refused, the file existing or not, since Godot asks every loader for it
    // with no existence test (resource_loader.cpp:284-293). A file one of them claims is classified as that loader reads
    // it and, when it has a .import sidecar, as the importer reads it too, since the loader may not recognize the type
    // hint or may fail, and the more restrictive answer wins.
    private static PreviewClassification Unremapped(string path, string extension)
    {
        bool ownClaimed = IsAny(extension, OwnLoaderExtensions) || IsAny(extension, BinaryExtensions);
        PreviewClassification? imported = FileAccess.FileExists(path + ".import") ? Imported(path, ownClaimed) : null;
        if (!ownClaimed)
        {
            return imported ?? new PreviewClassification(PreviewReferenceKind.UnknownExtension);
        }

        PreviewClassification own = OwnLoaded(path, extension);
        return imported is null || Restriction(own.Kind) > Restriction(imported.Kind) ? own : imported;
    }

    // What one of Godot's own loaders loads from the file: its content and the editor's class. A missing file holds
    // nothing to strip; Godot reports it as missing when the text loads.
    private static PreviewClassification OwnLoaded(string path, string extension)
    {
        if (!FileAccess.FileExists(path))
        {
            return new PreviewClassification(PreviewReferenceKind.Kept);
        }

        return new PreviewClassification(ByEditorClass(path, ByContent(path, extension) ?? PreviewReferenceKind.Kept));
    }

    // How restrictive a kind is: any refusal, then a script, then a text file the preview copies, then a kept file.
    private static int Restriction(PreviewReferenceKind kind) =>
        kind switch
        {
            PreviewReferenceKind.Kept => 0,
            PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource => 1,
            PreviewReferenceKind.Script => 2,
            _ => 3,
        };

    // The kind the file's head shows: RSRC or RSCC magic is a binary resource whatever the extension (the binary loader
    // reads the class from the header, resource_format_binary.cpp:1056-1095); a text file's first tag decides scene or
    // resource as the text loader does (resource_format_text.cpp:1163-1185, recognize :1388-1389). Null when the head
    // shows neither and the extension is not one only those loaders claim.
    private static PreviewReferenceKind? ByContent(string path, string extension)
    {
        byte[] head = Head(path);
        string magic = head.Length >= 4 ? Encoding.ASCII.GetString(head, 0, 4) : "";
        if (magic == "RSRC")
        {
            return BinaryResourceType(path) switch
            {
                null => PreviewReferenceKind.UnknownBinary,
                { } type when Inherits(type, PackedSceneClass) == true => PreviewReferenceKind.BinaryScene,
                { } type when Inherits(type, ScriptClass) == true => PreviewReferenceKind.Script,
                _ => PreviewReferenceKind.BinaryResource,
            };
        }

        if (magic == "RSCC" || IsAny(extension, BinaryExtensions))
        {
            return PreviewReferenceKind.UnknownBinary;
        }

        return FirstTag(head) switch
        {
            ("gd_scene", _) => PreviewReferenceKind.TextScene,
            ("gd_resource", { } type) when Inherits(type, ScriptClass) == true => PreviewReferenceKind.Script,
            ("gd_resource", _) => PreviewReferenceKind.TextResource,
            // The text loader claims a .tres whatever it holds; its reader refuses one that does not parse.
            _ when IsAny(extension, "tres") => PreviewReferenceKind.TextResource,
            _ => null,
        };
    }

    // The editor's class for the file can only tighten the kind: a Script class makes a kept file or a text resource a
    // script, a PackedScene makes a kept file a scene that cannot be stripped, and a GDExtension refuses it.
    private static PreviewReferenceKind ByEditorClass(string path, PreviewReferenceKind kind)
    {
        if (kind is not (PreviewReferenceKind.Kept or PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource))
        {
            return kind;
        }

        return GodotProjectLookups.Instance.ClassOf(path) switch
        {
            null => kind,
            { } cls when Inherits(cls, GDExtensionClass) == true => PreviewReferenceKind.NativeLibrary,
            { } cls when kind != PreviewReferenceKind.TextScene && Inherits(cls, ScriptClass) == true => PreviewReferenceKind.Script,
            { } cls when kind == PreviewReferenceKind.Kept && Inherits(cls, PackedSceneClass) == true => PreviewReferenceKind.OtherScene,
            _ => kind,
        };
    }

    // The file's kind with every file a remap may load in its place (ResourceLoader::_path_remap,
    // core/io/resource_loader.cpp:1442-1528): each translation target in internationalization/locale/translation_remaps,
    // the original as the fallback when a target is missing, and on each the path its .remap sidecar names. A script stays
    // a script, since its references are removed and nothing loads; otherwise every target must be inert, or a text file
    // the preview copies when the original is one too, since the copy loads in the original's place.
    private static PreviewClassification WithRemaps(string path, PreviewClassification own)
    {
        if (own.Kind == PreviewReferenceKind.Script)
        {
            return own;
        }

        List<string> candidates = [.. TranslationTargets(path), path];
        foreach (string candidate in candidates)
        {
            string loaded;
            if (FileAccess.FileExists(candidate + ".remap"))
            {
                (string? target, string? unsafeReason) = RemapSidecarPath(candidate);
                if (unsafeReason is not null)
                {
                    return new PreviewClassification(PreviewReferenceKind.UnsafeSidecar) { Reason = unsafeReason };
                }

                loaded = target ?? candidate;
            }
            else
            {
                loaded = candidate;
            }

            if (loaded == path)
            {
                continue;
            }

            PreviewClassification other = Classified(loaded, followRemaps: false);
            if (!IsSafeInPlaceOf(other.Kind, own.Kind))
            {
                return new PreviewClassification(PreviewReferenceKind.Remapped) { RemapTarget = loaded, RemapTargetClassification = other };
            }
        }

        return own;
    }

    private static bool IsSafeInPlaceOf(PreviewReferenceKind target, PreviewReferenceKind original) =>
        target == PreviewReferenceKind.Kept
        || (
            target is PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource
            && original is PreviewReferenceKind.TextScene or PreviewReferenceKind.TextResource
        );

    // The targets internationalization/locale/translation_remaps lists for the path, each "target:locale" cut at its last
    // ':' (resource_loader.cpp:1458-1462, load_translation_remaps :1562-1579), a uid:// target resolved.
    private static List<string> TranslationTargets(string path)
    {
        using Variant setting = ProjectSettings.HasSetting(TranslationRemapsSetting) ? ProjectSettings.GetSetting(TranslationRemapsSetting) : default;
        if (setting.VariantType != Variant.Type.Dictionary)
        {
            return [];
        }

        List<string> targets = [];
        using Godot.Collections.Dictionary remaps = setting.AsGodotDictionary();
        foreach ((Variant key, Variant value) in remaps)
        {
            using (key)
            using (value)
            {
                if (key.AsString() == path)
                {
                    targets.AddRange(value.AsStringArray().Select(TranslationTarget).OfType<string>());
                }
            }
        }

        return targets;
    }

    // A "target:locale" remap's target, null when it has no ':', which Godot skips.
    private static string? TranslationTarget(string remap)
    {
        int split = remap.LastIndexOf(':');
        return split < 0 ? null : UidResolved(remap[..split]);
    }

    // The path, a registered uid:// path resolved as ResourceUID::ensure_path does.
    private static string UidResolved(string path) => IsUid(path) ? RegisteredPath(path) ?? path : path;

    // The path a .remap sidecar names, read as _path_remap reads it (resource_loader.cpp:1504-1523): [remap] tags are
    // skipped and the first assignment ends the read, its value the target only when its key is path; null when it names
    // none. Never ConfigFile or VariantParser, which load a Resource() value. A sidecar that does not read as Godot reads
    // it, that holds a Resource() or Object() value Godot's own read would load, or whose path is not a plain string, is
    // unsafe.
    private static (string? Path, string? UnsafeReason) RemapSidecarPath(string path)
    {
        SidecarText sidecar = SidecarFile(path + ".remap");
        if (UnsafeSidecarReason(path, ".remap", sidecar) is { } reason)
        {
            return (null, reason);
        }

        if (sidecar.Entries.FirstOrDefault(entry => entry.Tag != "remap") is not { Tag: null, Key: "path" } first)
        {
            return (null, null);
        }

        return first.Value is { } target ? (UidResolved(target), null) : (null, $"{path}'s .remap sidecar has a path that is not a plain string");
    }

    // A sidecar file read as Godot reads it. Godot's StreamFile reads it a byte at a time, past a 0 byte that a decoded
    // string stops at (variant_parser.cpp:74-89, ustring.cpp:1782), so a sidecar holding one is unreadable; Godot writes
    // none, since its String cannot hold a 0. Otherwise the bytes are decoded as UTF-8 with nothing dropped: a leading
    // EF BB BF stays a U+FEFF, which StreamFile, doing no byte order mark handling either, puts into the first key
    // (variant_parser.cpp), so a sidecar that starts with one reads as unreadable.
    private static SidecarText SidecarFile(string sidecarPath)
    {
        byte[] bytes = FileAccess.GetFileAsBytes(sidecarPath);
        return Array.IndexOf(bytes, (byte)0) >= 0
            ? new SidecarText([], Unreadable: true, LoadsValue: false)
            : SidecarReader.Read(bytes.GetStringFromUtf8());
    }

    // Why a sidecar is unsafe to classify by: it does not read as Godot reads it, or it holds a value Godot loads or builds
    // as it reads it; null when neither.
    private static string? UnsafeSidecarReason(string path, string sidecarExtension, SidecarText sidecar) =>
        sidecar.Unreadable ? $"{path}'s {sidecarExtension} sidecar cannot be read as Godot reads it"
        : sidecar.LoadsValue ? $"{path}'s {sidecarExtension} sidecar holds a Resource() or Object() value, which Godot loads as it reads the sidecar"
        : null;

    // What the importer loads for the file, from its .import sidecar read as _get_path_and_type reads it (never
    // ConfigFile, whose parser loads a Resource() value, config_file.cpp:289; resource_importer.cpp:44-156). valid=false
    // only sets r_valid, which a load does not pass (:114-117, :174), so it changes nothing. type= PackedScene is an
    // imported scene, a Script class a script, a GDExtension a native library; each path= is classified as a file of its
    // own. A read that reaches the end returns OK with no type (:78-84), and the load goes ahead with an empty type hint
    // (:191), so for a file one of Godot's own loaders claims, a sidecar with no type= is classified by the paths it names;
    // null when it names none, or for any other file, which is then refused.
    private static PreviewClassification? Imported(string path, bool ownClaimed)
    {
        SidecarText sidecar = SidecarFile(path + ".import");
        if (UnsafeSidecarReason(path, ".import", sidecar) is { } reason)
        {
            return Unsafe(reason);
        }

        ImportRemap remap = ReadImportRemap(sidecar.Entries);
        if (remap.NotAString is { } key)
        {
            return Unsafe($"{path}'s .import sidecar has a {key} that is not a plain string");
        }

        if (remap.Type is { } type)
        {
            return ByImportedType(path, type) ?? ByImportedTargets(path, remap.Targets);
        }

        return ownClaimed && remap.Targets.Count > 0 ? ByImportedTargets(path, remap.Targets) : null;
    }

    // The keys the importer loads by: type, path and each path.<feature>; NotAString names the first of those whose value
    // is not a plain string.
    private sealed record ImportRemap(string? Type, List<string> Targets, string? NotAString);

    // The assignments _get_path_and_type reads (resource_importer.cpp:72-123): every one up to a tag not named remap, an
    // assignment with no key ending the read as such a tag does.
    private static ImportRemap ReadImportRemap(IReadOnlyList<SidecarEntry> entries)
    {
        string? type = null;
        List<string> targets = [];
        foreach (SidecarEntry entry in entries)
        {
            if (entry.Tag is not null || entry.Key.Length == 0)
            {
                if (entry.Tag == "remap")
                {
                    continue;
                }

                break;
            }

            if (!IsImportKey(entry.Key))
            {
                continue;
            }

            if (entry.Value is not { } literal)
            {
                return new ImportRemap(type, targets, entry.Key);
            }

            if (entry.Key == "type")
            {
                type = literal;
            }
            else
            {
                targets.Add(literal);
            }
        }

        return new ImportRemap(type, targets, null);
    }

    private static bool IsImportKey(string key) => key is "type" or "path" || key.StartsWith("path.", StringComparison.Ordinal);

    // What the importer's declared type alone makes of the file; null when its loaded files decide.
    private static PreviewClassification? ByImportedType(string path, string type) =>
        Inherits(type, ObjectClass) is null
            ? Unsafe($"{path}'s .import sidecar names the type {type}, which neither Godot nor the addon's metadata knows, so it may be a script")
        : Inherits(type, PackedSceneClass) == true ? new PreviewClassification(PreviewReferenceKind.ImportedScene)
        : Inherits(type, ScriptClass) == true ? new PreviewClassification(PreviewReferenceKind.Script)
        : Inherits(type, GDExtensionClass) == true ? new PreviewClassification(PreviewReferenceKind.NativeLibrary)
        : null;

    // The most restrictive answer for the files the importer may load: a refusal, else a script, else kept. The editor
    // loads a ".editor." variant when the import metadata says it has one (resource_importer.cpp, TOOLS_ENABLED).
    private static PreviewClassification ByImportedTargets(string path, List<string> targets)
    {
        IEnumerable<string> loaded = targets.SelectMany(target => new[] { target, $"{target.GetBaseName()}.editor.{target.GetExtension()}" });
        PreviewClassification[] found = [.. loaded.Select(target => ImportedTarget(path, target))];
        return found.FirstOrDefault(classification => classification.Kind is not (PreviewReferenceKind.Kept or PreviewReferenceKind.Script))
            ?? found.FirstOrDefault(classification => classification.Kind == PreviewReferenceKind.Script)
            ?? new PreviewClassification(PreviewReferenceKind.Kept);
    }

    // What a file the importer loads makes of the source, the file classified in full. A file with an .import of its own
    // the importer's load would hand to the importer again (resource_importer.cpp:191), which the preview does not follow.
    // A scene of any kind is an imported scene, a text resource, which the preview cannot strip in place, is refused, and
    // a binary resource is kept only as the importer's output under res://.godot/imported/, a kept file or a script
    // stands, and any other refusal is the target's, carried with it so the message names it. A target with a ".." segment
    // or a '\' is refused unread.
    private static PreviewClassification ImportedTarget(string source, string target)
    {
        // FileAccess::fix_path resolves no ".." (file_access.cpp:254-277), so such a target is not where it says it is.
        if (target.Contains('\\', StringComparison.Ordinal) || target.Split('/').Contains(".."))
        {
            return Unsafe($"{source} is imported to {target}, a path that leaves Godot's importer folder");
        }

        if (FileAccess.FileExists(target + ".import"))
        {
            return Unsafe($"{source} is imported to {target}, which has an .import sidecar of its own");
        }

        PreviewClassification classification = Classified(target, followRemaps: false);
        return classification.Kind switch
        {
            PreviewReferenceKind.TextScene
            or PreviewReferenceKind.BinaryScene
            or PreviewReferenceKind.OtherScene
            or PreviewReferenceKind.ImportedScene => new PreviewClassification(PreviewReferenceKind.ImportedScene),
            PreviewReferenceKind.TextResource => Unsafe($"{source} is imported to {target}, a text resource the preview cannot strip in place"),
            PreviewReferenceKind.BinaryResource when target.StartsWith(ImporterOutputFolder, StringComparison.Ordinal) => new PreviewClassification(
                PreviewReferenceKind.Kept
            ),
            PreviewReferenceKind.Kept or PreviewReferenceKind.Script => classification,
            _ => new PreviewClassification(classification.Kind) { ImportTarget = target, ImportTargetClassification = classification },
        };
    }

    private static PreviewClassification Unsafe(string reason) => new(PreviewReferenceKind.UnsafeSidecar) { Reason = reason };

    // A text file's first tag, after a byte order mark, whitespace and ';' comment lines: its name and its fields.
    [GeneratedRegex("""^\uFEFF?(?:\s|;[^\n]*(?:\n|$))*\[([^\s\]]+)([^\]]*)\]""")]
    private static partial Regex FirstTagPattern();

    [GeneratedRegex("(?:^|\\s)type=\"([^\"]*)\"")]
    private static partial Regex TypeFieldPattern();

    // The first tag of a text file and its type= field, as the text loader reads it: whitespace and ';' comment lines
    // skipped, then "[name field=value …]"; null when the head starts with anything else.
    private static (string Name, string? Type)? FirstTag(byte[] head)
    {
        if (FirstTagPattern().Match(Encoding.UTF8.GetString(head)) is not { Success: true } tag)
        {
            return null;
        }

        Match type = TypeFieldPattern().Match(tag.Groups[2].Value);
        return (tag.Groups[1].Value, type.Success ? type.Groups[1].Value : null);
    }

    // The file's first bytes, for its magic and its first tag.
    private static byte[] Head(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file is null ? [] : file.GetBuffer((long)Math.Min((ulong)HeadLength, file.GetLength()));
    }

    private static bool IsUid(string? path) => path?.StartsWith(UidScheme, StringComparison.Ordinal) == true;

    private static bool IsAny(string extension, params string[] candidates) =>
        candidates.Any(candidate => candidate.Equals(extension, StringComparison.OrdinalIgnoreCase));

    private static string? RegisteredPath(string uidText)
    {
        long id = ResourceUid.TextToId(uidText);
        return id != ResourceUid.InvalidId && ResourceUid.HasId(id) ? ResourceUid.GetIdPath(id) : null;
    }

    // The extensions only a script loader claims: ResourceFormatLoaderGDScript (modules/gdscript/gdscript_resource_format.cpp)
    // for gd and gdc, and ResourceFormatLoaderCSharpScript (modules/mono/csharp_script_resource_format.cpp) for cs. The table
    // is fixed, never ResourceLoader.GetRecognizedExtensionsForType, which asks every registered loader, project loaders
    // included, for its extensions and so runs their script's code: a preview with scripts off runs none of it. An extension
    // another script language claims is refused as unknown instead, which fails closed.
    private static readonly FrozenSet<string> ScriptExtensions = new[] { "gd", "gdc", "cs" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the class inherits the base class (or is it), asked of the running <c>ClassDB</c> first, which holds the
    /// GDExtension classes loaded now, and of the addon's bundled metadata only for a class <c>ClassDB</c> lacks;
    /// <see cref="ClassProbe"/>, when set, answers in front of both.
    /// </summary>
    /// <param name="type">A class name.</param>
    /// <param name="baseClass">The base class's name.</param>
    /// <returns>Whether it inherits it; null when neither knows the class.</returns>
    internal static bool? Inherits(string type, string baseClass)
    {
        if (string.Equals(type, baseClass, StringComparison.Ordinal))
        {
            return true;
        }

        if (ClassProbe?.Invoke(type, baseClass) is { } probed)
        {
            return probed;
        }

        using (var typeName = new StringName(type))
        {
            if (ClassDB.ClassExists(typeName))
            {
                using var baseName = new StringName(baseClass);
                return ClassDB.IsParentClass(typeName, baseName);
            }
        }

        GodotMetadata metadata = XscnMetadata.Value;
        return metadata.FindClass(type) is { } cls ? metadata.Inherits(cls, baseClass) : null;
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

    /// <summary>One entry of a sidecar as <c>VariantParser::parse_tag_assign_eof</c> gives it: a tag, or an assignment.</summary>
    /// <param name="Tag">The tag's name; null for an assignment.</param>
    /// <param name="Key">The assignment's key; empty for a tag.</param>
    /// <param name="Value">The assignment's value when it is one plain <c>"…"</c> string, decoded; else null.</param>
    private sealed record SidecarEntry(string? Tag, string Key, string? Value);

    /// <summary>A sidecar read as Godot reads it.</summary>
    /// <param name="Entries">Its tags and assignments, in order, up to where it stopped reading.</param>
    /// <param name="Unreadable">Whether some of it does not tokenize as Godot's text-variant syntax.</param>
    /// <param name="LoadsValue">
    /// Whether a value anywhere in it holds a <c>Resource</c>, <c>ExtResource</c>, <c>SubResource</c> or <c>Object</c>
    /// identifier, which VariantParser loads or builds as it parses.
    /// </param>
    private sealed record SidecarText(IReadOnlyList<SidecarEntry> Entries, bool Unreadable, bool LoadsValue);

    /// <summary>
    /// Reads a <c>.import</c> or <c>.remap</c> sidecar as Godot's text-variant parser does
    /// (core/variant/variant_parser.cpp), loading nothing. Keys as <c>parse_tag_assign_eof</c> builds them (:1891-1960):
    /// <c>;</c> starts a comment, a <c>[</c> before any key text starts a simple tag read to its <c>]</c> and stripped,
    /// a <c>"…"</c> string is the whole key, <c>=</c> ends it, and every other character above 32 is appended. Values
    /// token by token as <c>get_token</c> reads them (:180-520), skipped without being evaluated. The whole text is read,
    /// past the part Godot's load stops at, since the editor's own reads go further.
    /// </summary>
    private sealed class SidecarReader
    {
        // The identifiers whose value VariantParser::parse_value loads or builds as it parses.
        private static readonly HashSet<string> LoadingIdentifiers = new(StringComparer.Ordinal)
        {
            "Resource",
            "ExtResource",
            "SubResource",
            "Object",
        };

        // The identifiers parse_value takes as a whole value (variant_parser.cpp:696-708); any other is a constructor.
        private static readonly HashSet<string> LiteralIdentifiers = new(StringComparer.Ordinal)
        {
            "true",
            "false",
            "null",
            "nil",
            "inf",
            "-inf",
            "inf_neg",
            "nan",
        };

        // The escapes get_token decodes as one character, and those the preview takes as themselves; any other is refused.
        private static readonly Dictionary<char, int> Escapes = new()
        {
            ['b'] = 8,
            ['t'] = 9,
            ['n'] = 10,
            ['f'] = 12,
            ['r'] = 13,
            ['"'] = '"',
            ['\\'] = '\\',
            ['\''] = '\'',
        };

        private const string Openers = "[{(";

        private const string Closers = "]})";

        private const string Punctuation = "[]{}():,.=";

        private readonly string text;

        private readonly List<SidecarEntry> entries = [];

        private int at;

        private bool failed;

        private bool loadsValue;

        private SidecarReader(string text) => this.text = text;

        private enum TokenKind
        {
            End,
            Punctuation,
            String,
            StringName,
            Number,
            Color,
            Identifier,
        }

        private readonly record struct Token(TokenKind Kind, string Text);

        /// <summary>Reads the sidecar's text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>What it holds.</returns>
        internal static SidecarText Read(string text)
        {
            var reader = new SidecarReader(text);
            bool more = true;
            while (more && !reader.failed)
            {
                more = reader.ReadEntry();
            }

            return new SidecarText(reader.entries, reader.failed, reader.loadsValue);
        }

        // One call of parse_tag_assign_eof: false at the end of the text.
        private bool ReadEntry()
        {
            var key = new StringBuilder();
            while (at < text.Length)
            {
                if (KeyCharacter(text[at++], key) is { } entry)
                {
                    entries.Add(entry);
                    return true;
                }
            }

            return false;
        }

        // One character of a key; the entry when it ends one.
        private SidecarEntry? KeyCharacter(char c, StringBuilder key)
        {
            switch (c)
            {
                case ';':
                    SkipComment();
                    return null;
                case '[' when key.Length == 0:
                    return new SidecarEntry(ReadTag(), "", null);
                case '"':
                    key.Clear().Append(ReadQuoted());
                    return null;
                case '=':
                    return new SidecarEntry(null, Checked(key.ToString()), ReadValue());
                default:
                    if (c > ' ')
                    {
                        key.Append(c);
                    }

                    return null;
            }
        }

        // A simple tag's name, read to the ']' no '\' escapes, its edges stripped (_parse_tag with p_simple_tag).
        private string ReadTag()
        {
            var name = new StringBuilder();
            bool escaping = false;
            while (at < text.Length)
            {
                char c = text[at++];
                if (c == ']' && !escaping)
                {
                    return Checked(name.ToString().Trim(EdgeCharacters));
                }

                escaping = c == '\\';
                name.Append(c);
            }

            Fail();
            return "";
        }

        // The characters String::strip_edges takes off: every one up to and including the space.
        private static readonly char[] EdgeCharacters = [.. Enumerable.Range(0, 33).Select(code => (char)code)];

        // One value, skipped; its text when it is one plain string.
        private string? ReadValue()
        {
            Token first = NextToken();
            SkipValue(first);
            return first.Kind == TokenKind.String && !failed ? first.Text : null;
        }

        // Skips the rest of a value whose first token is given, as parse_value reads one: a string, a number or a color
        // alone, an identifier with its optional [type] and (arguments), or a bracketed array or dictionary.
        private void SkipValue(Token first)
        {
            switch (first.Kind)
            {
                case TokenKind.String or TokenKind.StringName or TokenKind.Number or TokenKind.Color:
                    return;
                case TokenKind.Identifier:
                    SkipConstructed(first.Text);
                    return;
                case TokenKind.Punctuation when first.Text is "[" or "{":
                    SkipGroup(first.Text[0]);
                    return;
                default:
                    Fail();
                    return;
            }
        }

        // An identifier value: a literal alone, else a constructor, Array[int]([…]) or Vector2(…), whose arguments follow.
        private void SkipConstructed(string identifier)
        {
            Note(identifier);
            if (LiteralIdentifiers.Contains(identifier))
            {
                return;
            }

            if (NextIs('['))
            {
                SkipGroup('[');
            }

            if (!NextIs('('))
            {
                Fail();
                return;
            }

            SkipGroup('(');
        }

        private void Note(string identifier) => loadsValue |= LoadingIdentifiers.Contains(identifier);

        // Whether the next token is the bracket, consumed when it is; nothing is consumed when it is not.
        private bool NextIs(char bracket)
        {
            int start = at;
            SkipBlank();
            if (at < text.Length && text[at] == bracket)
            {
                at++;
                return true;
            }

            at = start;
            return false;
        }

        // Skips tokens to the bracket that closes the one just read, nested brackets matched.
        private void SkipGroup(char open)
        {
            var closing = new Stack<char>();
            closing.Push(Closers[Openers.IndexOf(open, StringComparison.Ordinal)]);
            while (!failed && closing.Count > 0)
            {
                GroupToken(NextToken(), closing);
            }
        }

        private void GroupToken(Token token, Stack<char> closing)
        {
            char c = token.Kind == TokenKind.Punctuation ? token.Text[0] : '\0';
            if (token.Kind == TokenKind.Identifier)
            {
                Note(token.Text);
            }
            else if (token.Kind == TokenKind.End)
            {
                Fail();
            }
            else if (Openers.Contains(c, StringComparison.Ordinal))
            {
                closing.Push(Closers[Openers.IndexOf(c, StringComparison.Ordinal)]);
            }
            else if (Closers.Contains(c, StringComparison.Ordinal) && closing.Pop() != c)
            {
                Fail();
            }
        }

        // One token as get_token reads it, after whitespace and comments; End at the end of the text or on a failure.
        private Token NextToken()
        {
            SkipBlank();
            char c = NextChar();
            return c switch
            {
                '\0' => new Token(TokenKind.End, ""),
                '"' => new Token(TokenKind.String, ReadQuoted()),
                '&' or '@' => StringName(),
                '#' => new Token(TokenKind.Color, ReadWhile(char.IsAsciiHexDigit)),
                _ when Punctuation.Contains(c, StringComparison.Ordinal) => new Token(TokenKind.Punctuation, c.ToString()),
                _ => Word(c),
            };
        }

        // A StringName, &"…" or the 3.x @"…", after its '&' or '@'.
        private Token StringName() => NextChar() == '"' ? new Token(TokenKind.StringName, ReadQuoted()) : Failed();

        // A number or an identifier, '-' allowed before either (get_token's default branch); anything else fails, as
        // Godot reports an unexpected character.
        private Token Word(char c)
        {
            int start = at - 1;
            char first = c == '-' ? NextChar() : c;
            if (char.IsAsciiDigit(first))
            {
                SkipNumber();
                return new Token(TokenKind.Number, text[start..at]);
            }

            if (char.IsAsciiLetter(first) || first == '_')
            {
                ReadWhile(next => char.IsAsciiLetterOrDigit(next) || next == '_');
                return new Token(TokenKind.Identifier, text[start..at]);
            }

            return Failed();
        }

        // The rest of a number: digits, a decimal point and an exponent with its sign.
        private void SkipNumber()
        {
            while (at < text.Length && (IsNumberCharacter(text[at]) || (text[at] is '+' or '-' && text[at - 1] is 'e' or 'E')))
            {
                at++;
            }
        }

        private static bool IsNumberCharacter(char c) => char.IsAsciiDigit(c) || c is '.' or 'e' or 'E';

        private string ReadWhile(Func<char, bool> take)
        {
            int start = at;
            while (at < text.Length && take(text[at]))
            {
                at++;
            }

            return text[start..at];
        }

        // A string's text after its opening quote, escapes decoded as get_token decodes them (:277-413). Godot narrows each
        // code to a byte and decodes the bytes again (ustring.cpp:1706-1713, :1756-1766), so a \u or \U escape outside
        // 0x01-0x7F fails rather than be decoded otherwise, as does an unterminated string or an unknown escape.
        private string ReadQuoted()
        {
            var value = new StringBuilder();
            while (!failed)
            {
                char c = NextChar();
                if (c == '"')
                {
                    break;
                }

                if (c == '\0')
                {
                    Fail();
                }
                else
                {
                    value.Append(c == '\\' ? (char)Math.Max(Escaped(), 0) : c);
                }
            }

            return Checked(value.ToString());
        }

        // The text, failed when it holds a byte order mark or a replacement character, either of which Godot's narrowing
        // and decoding reads otherwise.
        private string Checked(string read)
        {
            if (read.Contains('\uFEFF', StringComparison.Ordinal) || read.Contains('\uFFFD', StringComparison.Ordinal))
            {
                Fail();
            }

            return read;
        }

        // The code an escape names, after its '\'; -1, failed, when it names none.
        private int Escaped()
        {
            char next = NextChar();
            return next is 'u' or 'U' ? Hex(next == 'u' ? 4 : 6)
                : Escapes.TryGetValue(next, out int code) ? code
                : Fail();
        }

        private int Hex(int digits)
        {
            int code = 0;
            for (int i = 0; i < digits; i++)
            {
                char c = NextChar();
                if (!char.IsAsciiHexDigit(c))
                {
                    return Fail();
                }

                code = (code << 4) | Convert.ToInt32(c.ToString(), 16);
            }

            return code is >= 0x01 and <= 0x7F ? code : Fail();
        }

        // Whitespace and ';' comments, which get_token skips between tokens.
        private void SkipBlank()
        {
            while (at < text.Length && (text[at] is > '\0' and <= ' ' || text[at] == ';'))
            {
                if (text[at] == ';')
                {
                    SkipComment();
                }
                else
                {
                    at++;
                }
            }
        }

        // To the end of the line, a comment's end.
        private void SkipComment()
        {
            int end = text.IndexOf('\n', at);
            at = end < 0 ? text.Length : end + 1;
        }

        private char NextChar() => at < text.Length ? text[at++] : '\0';

        private int Fail()
        {
            failed = true;
            at = text.Length;
            return -1;
        }

        private Token Failed()
        {
            Fail();
            return new Token(TokenKind.End, "");
        }
    }
}
