#if TOOLS
using System;
using System.IO;
using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// Exports each <c>.xscn</c> and <c>.xres</c> as Godot binary (<c>.scn</c>/<c>.res</c>) behind a <c>.remap</c>, so the
/// exported game loads them with Godot's own binary loader and the XML is not shipped.
/// </summary>
/// <remarks>
/// A failure is reported as an export error message. Godot gives an export plugin no other way to fail an export:
/// <c>EditorExportPlatform::export_project_files</c> never reads the plugins' messages, and a headless
/// <c>--export-pack</c> exits 0 when the only failure is a message (<c>editor/editor_node.cpp</c>, the export_defer
/// branch: <c>EXIT_FAILURE</c> only when the export returned an error). The editor's export dialog shows such an export
/// as "Failed.", and a headless run prints the message as an <c>ERROR: godot-xaml:</c> line.
/// </remarks>
[Tool]
public partial class XscnExportPlugin : EditorExportPlugin
{
    private const string Category = "godot-xaml";
    private const string ExportFolder = "res://.godot/exported/godot-xaml/";
    private const string DropError = "the export would drop the godot-xaml loaders";

    /// <inheritdoc/>
    public override string _GetName() => Category;

    /// <inheritdoc/>
    public override void _ExportBegin(string[] features, bool isDebug, string path, uint flags)
    {
        string? problem = LoaderDropProblem(GetExportPreset());
        if (problem is not null)
        {
            Fail(problem);
        }
    }

    /// <summary>Why the preset's export would leave out a loader's script, or <see langword="null"/> when it would not.</summary>
    /// <remarks>
    /// Mirrors <c>EditorExportPlatform::export_project_files</c>: the export mode picks the files, the include filter
    /// adds to them and the exclude filter, applied last, removes from them. A dropped script also drops its class from
    /// the exported global class list, so the game never registers that loader.
    /// </remarks>
    private static string? LoaderDropProblem(EditorExportPreset preset)
    {
        string excludeFilter = preset.GetExcludeFilter();
        foreach (string script in XscnFormatLoader.LoaderScripts)
        {
            string? pattern = MatchingPattern(excludeFilter, script);
            if (pattern is not null)
            {
                return $"{DropError}: the preset's exclude filter pattern \"{pattern}\" matches {script}; remove it from the "
                    + "preset's exclude filter (Resources tab, \"Filters to exclude files/folders from project\").";
            }
        }

        string includeFilter = preset.GetIncludeFilter();
        foreach (string script in XscnFormatLoader.LoaderScripts)
        {
            if (!ExportedByMode(preset, script) && MatchingPattern(includeFilter, script) is null)
            {
                return $"{DropError}: {script} is not exported; add addons/godot_xaml/*.cs to the preset's include filter "
                    + "(Resources tab, \"Filters to export non-resource files/folders\").";
            }
        }

        return null;
    }

    /// <summary>Whether the preset's export mode alone exports the file, before the include and exclude filters.</summary>
    private static bool ExportedByMode(EditorExportPreset preset, string path) =>
        preset.GetExportFilter() switch
        {
            EditorExportPreset.ExportFilter.ExportAllResources => true,
            EditorExportPreset.ExportFilter.ExcludeSelectedResources => !preset.HasExportFile(path),
            EditorExportPreset.ExportFilter.ExportCustomized => CustomizedKeeps(preset, path),
            EditorExportPreset.ExportFilter.ExportSelectedResources => preset.HasExportFile(path),
            // Selected scenes skips every ticked file that is not a PackedScene, which a script never is.
            _ => false,
        };

    /// <summary>
    /// Whether a customized preset keeps the file: each folder from <c>res://</c> down inherits its parent's mode unless
    /// it sets its own, as <c>EditorExportPlatform::_export_find_customized_resources</c> walks them.
    /// </summary>
    private static bool CustomizedKeeps(EditorExportPreset preset, string path)
    {
        string folder = "res://";
        EditorExportPreset.FileExportMode mode = preset.GetFileExportMode(folder);
        string[] parts = path.TrimPrefix("res://").Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            folder += parts[i] + "/";
            mode = preset.GetFileExportMode(folder, mode);
        }

        return preset.GetFileExportMode(path, mode) != EditorExportPreset.FileExportMode.Remove;
    }

    /// <inheritdoc/>
    public override void _ExportFile(string path, string type, string[] features)
    {
        string extension = path.GetExtension();
        string? binaryExtension =
            extension.Equals("xscn", StringComparison.OrdinalIgnoreCase) ? "scn"
            : extension.Equals("xres", StringComparison.OrdinalIgnoreCase) ? "res"
            : null;
        if (binaryExtension is null)
        {
            return;
        }

        try
        {
            string? reason = ExportBinary(path, binaryExtension);
            if (reason is not null)
            {
                Fail($"{path}: {reason}");
                Skip();
            }
        }
        // An exception escaping a virtual only logs; the export would go on and ship the raw XML.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Fail($"{path}: {exception}");
            Skip();
        }
    }

    /// <summary>Loads the file through the registered loader, saves it as binary and adds it in its place.</summary>
    /// <returns>Why the file could not be exported, or <see langword="null"/> once it was added.</returns>
    internal string? ExportBinary(string path, string binaryExtension)
    {
        Resource? resource = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore);
        if (resource is null)
        {
            return "the file did not load; the errors above say why.";
        }

        try
        {
            // Where Godot saves its own export conversions (editor_export_platform.cpp:1090, 1481), not the shared OS temp folder.
            // The saver gets the absolute path: for a res:// one the editor's save callback writes a folding file in .godot/editor/
            // that is never removed (resource_saver.cpp:146), and an absolute path is no resource file, so no uid is minted for it.
            string tempPath = ProjectSettings.GlobalizePath($"{ExportFolder}{Guid.NewGuid():N}.{binaryExtension}");
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(ExportFolder));
            try
            {
                Error saved = ResourceSaver.Save(resource, tempPath);
                if (saved != Error.Ok)
                {
                    return $"saving it as Godot binary failed: {saved}.";
                }

                byte[] bytes = File.ReadAllBytes(tempPath);
                // Named as Godot names its own .tscn conversions, so a project's a.scn beside a.xscn is not overwritten.
                AddFile($"{ExportFolder}export-{path.Md5Text()}-{path.GetFile().GetBaseName()}.{binaryExtension}", bytes, remap: true);
                return null;
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            // The ignored-cache load made this wrapper the only holder of a fresh resource; left to its finalizer, it can be
            // released after the engine has shut down (GXAML-81). A script's code may have handed the wrapper out while the
            // file loaded, so, as the load route does, it is released only for a resource with no script.
            using Variant script = resource.GetScript();
            if (script.VariantType == Variant.Type.Nil)
            {
                resource.Dispose();
            }
        }
    }

    /// <summary>The first pattern of an include or exclude filter that matches the file, matched as Godot matches it.</summary>
    /// <remarks>
    /// <c>EditorExportPlatform::_edit_filter_list</c> splits the filter at commas, trims each pattern and matches it,
    /// case-insensitively, against the path with and without <c>res://</c>.
    /// </remarks>
    /// <returns>The pattern, or <see langword="null"/> when none matches.</returns>
    private static string? MatchingPattern(string filter, string resPath)
    {
        string bare = resPath.TrimPrefix("res://");
        foreach (string part in filter.Split(','))
        {
            string pattern = part.StripEdges();
            if (pattern.Length > 0 && (resPath.MatchN(pattern) || bare.MatchN(pattern)))
            {
                return pattern;
            }
        }

        return null;
    }

    private void Fail(string message) => GetExportPlatform().AddMessage(EditorExportPlatform.ExportMessageType.Error, Category, message);
}
#endif
