#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotXaml.Addon;

/// <summary>The addon's editor plugin; it registers the export plugin and keeps the global class snapshot current.</summary>
/// <remarks>
/// The loader and saver register as global classes, never from here. Godot builds the loaders at startup from the previous
/// session's class cache (<c>main/main.cpp:4494</c>), before the editor's first scan stores the fresh list
/// (<c>editor/file_system/editor_file_system.cpp:336-343</c>), so the plugin rebuilds the snapshot itself when it enters
/// the tree, and again on each <c>script_classes_updated</c>. Deleting a script in the FileSystem dock emits it twice:
/// first from <c>FileSystemDock::_resource_removed</c> (<c>editor/docks/filesystem_dock.cpp:1806-1811</c>), which
/// <c>DependencyRemoveDialog::ok_pressed</c> reaches before it updates the deleted file
/// (<c>editor/file_system/dependency_editor.cpp:782-818</c>), with the loaders not yet re-created; then from the update's
/// <c>EditorFileSystem::_update_script_classes</c>, which re-creates them (<c>editor/file_system/editor_file_system.cpp:2195-2205</c>).
/// Between the two, only this hook keeps the snapshot right. The hook is connected by object and method name, not by a C#
/// delegate: while an assembly reload has the plugin's script removed (<c>modules/mono/csharp_script.cpp:781-786</c>), the
/// engine skips the slot as invalid (<c>core/variant/callable.cpp:153-158</c>, <c>core/object/object.cpp:1255-1257</c>), and
/// once the reload gives the tool script a new instance (<c>modules/mono/csharp_script.cpp:943-951</c>) the same connection
/// calls it again.
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
        EditorInterface
            .Singleton.GetResourceFilesystem()
            .Connect(EditorFileSystem.SignalName.ScriptClassesUpdated, new Callable(this, MethodName.OnScriptClassesUpdated));
        exportPlugin = new XscnExportPlugin();
        AddExportPlugin(exportPlugin);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        EditorFileSystem fileSystem = EditorInterface.Singleton.GetResourceFilesystem();
        var hook = new Callable(this, MethodName.OnScriptClassesUpdated);
        if (fileSystem.IsConnected(EditorFileSystem.SignalName.ScriptClassesUpdated, hook))
        {
            fileSystem.Disconnect(EditorFileSystem.SignalName.ScriptClassesUpdated, hook);
        }

        if (exportPlugin is null)
        {
            return;
        }

        RemoveExportPlugin(exportPlugin);
        exportPlugin = null;
    }

    // An instance method, so the engine finds it on this object by name.
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Godot calls it through a Callable on this object.")]
    private void OnScriptClassesUpdated() => GlobalClassSnapshot.Rebuild();
}
#endif
