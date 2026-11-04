using Godot;
using Godot.Collections;
using GodotXaml.Core.Conversion;

namespace GodotXaml.Addon;

/// <summary>What the conversion asks of the open project: a file's resource class and a script's global class name.</summary>
internal sealed class GodotProjectLookups : IExtResourceTypes
{
    /// <summary>The one instance; it holds no state.</summary>
    public static GodotProjectLookups Instance { get; } = new();

    /// <summary>
    /// The class the editor's filesystem records for the file, asked on the main thread only: the editor's filesystem
    /// lookup takes no lock and can add a folder to the tree the main thread updates, so a loader worker thread does not
    /// ask. Off the main thread and outside the editor (a game run, an exported game) there is no answer, and the
    /// conversion falls back to the property's declared type, then to the file's extension.
    /// </summary>
    /// <param name="path">The file's <c>res://</c> path.</param>
    /// <returns>The class name, or null when it is not known.</returns>
    public string? ClassOf(string path)
    {
#if TOOLS
        if (Engine.IsEditorHint() && OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            string type = EditorInterface.Singleton.GetResourceFilesystem().GetFileType(path);
            return type.Length > 0 ? type : null;
        }
#endif
        return null;
    }

    /// <summary>
    /// The global class name (<c>class_name</c>) of the script at a path, from the project's global class list, asked on
    /// the main thread only. <c>ProjectSettings::get_global_class_list</c> returns the list the main thread replaces in
    /// <c>store_global_class_list</c> with no lock (<c>core/config/project_settings.cpp:1446-1480</c>):
    /// <code>
    /// if (is_global_class_list_loaded) { return global_class_list; }
    /// ...
    /// global_class_list = p_classes;
    /// </code>
    /// and <c>_GetDependencies</c> runs on the editor's scan thread (<c>editor/file_system/editor_file_system.cpp:565-567,
    /// 1366</c>: <c>sd-&gt;_scan_filesystem();</c>, <c>fi-&gt;deps = _get_dependencies(path);</c>), loads on loader threads.
    /// Off the main thread there is no answer, and the converted header carries no <c>script_class</c>.
    /// </summary>
    /// <param name="scriptPath">The script's <c>res://</c> path.</param>
    /// <returns>The class name, or null when the script declares none or the caller is not on the main thread.</returns>
    public static string? ScriptClassOf(string scriptPath)
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            return null;
        }

        // Each Godot value is disposed as it is read: one left to its finalizer can be destroyed after the engine has shut
        // down (GXAML-81). The typed list's cast returns its untyped Array, which can be.
        using Variant pathKey = "path";
        using Variant classKey = "class";
        using var list = (Godot.Collections.Array)ProjectSettings.GetGlobalClassList();
        foreach (Variant item in list)
        {
            using (item)
            {
                using Dictionary entry = item.AsGodotDictionary();
                using Variant path = entry[pathKey];
                if (path.AsString() == scriptPath)
                {
                    using Variant name = entry[classKey];
                    return name.AsString();
                }
            }
        }

        return null;
    }
}
