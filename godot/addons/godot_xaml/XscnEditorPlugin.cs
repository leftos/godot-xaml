#if TOOLS
using Godot;

namespace GodotXaml.Addon;

/// <summary>The addon's editor plugin; it registers the export plugin and keeps the global class snapshot current.</summary>
/// <remarks>
/// The loader and saver register as global classes, never from here. Godot builds the loaders at startup from the previous
/// session's class cache (<c>main/main.cpp:4494</c>), before the editor's first scan stores the fresh list
/// (<c>editor/file_system/editor_file_system.cpp:336-343</c>), so the plugin rebuilds the snapshot itself when it enters
/// the tree, and again on each <c>script_classes_updated</c>, which a deleted script emits without re-creating the loaders
/// (<c>editor/docks/filesystem_dock.cpp:1808-1811</c>).
/// </remarks>
[Tool]
public partial class XscnEditorPlugin : EditorPlugin
{
    private XscnExportPlugin? exportPlugin;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        // Plugins enter the tree at editor_file_system.cpp:360, after the first scan stored the fresh class list at :343 and
        // before the threaded scan at :1121, which asks a loader for a new .xres's script class at :1357.
        GlobalClassSnapshot.Rebuild();
        EditorInterface.Singleton.GetResourceFilesystem().ScriptClassesUpdated += OnScriptClassesUpdated;
        exportPlugin = new XscnExportPlugin();
        AddExportPlugin(exportPlugin);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        EditorInterface.Singleton.GetResourceFilesystem().ScriptClassesUpdated -= OnScriptClassesUpdated;
        if (exportPlugin is null)
        {
            return;
        }

        RemoveExportPlugin(exportPlugin);
        exportPlugin = null;
    }

    private void OnScriptClassesUpdated() => GlobalClassSnapshot.Rebuild();
}
#endif
