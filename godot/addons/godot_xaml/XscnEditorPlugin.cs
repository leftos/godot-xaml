#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// The addon's editor plugin; it registers the export plugin, keeps the global class snapshot current and runs the preview
/// server.
/// </summary>
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
/// <para>
/// The plugin owns the preview server (<see cref="XscnPreviewServer"/>): it runs while
/// <see cref="XscnPreviewServer.EnabledSetting"/> is on, starting and stopping as the setting changes, is pumped from
/// <see cref="_Process"/>, and stops when the plugin leaves the tree and before an assembly reload, which never calls
/// <see cref="_ExitTree"/> (<c>modules/mono/csharp_script.cpp:768</c>, <c>943-951</c>).
/// </para>
/// </remarks>
[Tool]
public partial class XscnEditorPlugin : EditorPlugin, ISerializationListener
{
    private XscnExportPlugin? exportPlugin;

    private XscnPreviewServer? previewServer;

    // Set when a start failed, so each settings change does not warn again; cleared when the setting turns off.
    private bool previewStartFailed;

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
        RegisterPreviewSetting();
        ConnectSettingsHook();
        FollowPreviewSetting();
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => previewServer?.Pump();

    /// <summary>
    /// Stops the preview server before an assembly reload unloads this class, and disconnects the settings hook, so nothing
    /// starts it during the reload; <see cref="OnAfterDeserialize"/> follows the setting again once the new class is in.
    /// </summary>
    public void OnBeforeSerialize()
    {
        DisconnectSettingsHook();
        StopPreviewServer();
    }

    /// <summary>
    /// Reconnects the settings hook and starts a new preview server when the setting is on, since <see cref="_EnterTree"/>
    /// does not run again after an assembly reload and the stopped server cannot restart.
    /// </summary>
    public void OnAfterDeserialize()
    {
        ConnectSettingsHook();
        previewStartFailed = false;
        FollowPreviewSetting();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        DisconnectSettingsHook();
        StopPreviewServer();
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

    // The setting is a bool in the Project Settings dialog, off by default; a project that never sets it saves nothing.
    private static void RegisterPreviewSetting()
    {
        if (!ProjectSettings.HasSetting(XscnPreviewServer.EnabledSetting))
        {
            ProjectSettings.SetSetting(XscnPreviewServer.EnabledSetting, false);
        }

        ProjectSettings.SetInitialValue(XscnPreviewServer.EnabledSetting, false);
        using var info = new Godot.Collections.Dictionary { { "name", XscnPreviewServer.EnabledSetting }, { "type", (int)Variant.Type.Bool } };
        ProjectSettings.AddPropertyInfo(info);
    }

    private static bool PreviewServerEnabled()
    {
        using Variant fallback = false;
        using Variant setting = ProjectSettings.GetSetting(XscnPreviewServer.EnabledSetting, fallback);
        return setting.AsBool();
    }

    // Connected by name, as the class hook is, so the engine finds it on this object.
    private void OnSettingsChanged() => FollowPreviewSetting();

    private void FollowPreviewSetting()
    {
        if (!PreviewServerEnabled())
        {
            previewStartFailed = false;
            StopPreviewServer();
            return;
        }

        if (previewServer is not null || previewStartFailed)
        {
            return;
        }

        var server = new XscnPreviewServer(XscnPreviewDiscovery.DefaultFolder(), ProjectSettings.GlobalizePath("res://"), this);
        if (server.Start())
        {
            previewServer = server;
            return;
        }

        previewStartFailed = true;
        server.Dispose();
    }

    private void ConnectSettingsHook()
    {
        var settingsHook = new Callable(this, MethodName.OnSettingsChanged);
        if (!ProjectSettings.Singleton.IsConnected(ProjectSettings.SignalName.SettingsChanged, settingsHook))
        {
            ProjectSettings.Singleton.Connect(ProjectSettings.SignalName.SettingsChanged, settingsHook);
        }
    }

    private void DisconnectSettingsHook()
    {
        var settingsHook = new Callable(this, MethodName.OnSettingsChanged);
        if (ProjectSettings.Singleton.IsConnected(ProjectSettings.SignalName.SettingsChanged, settingsHook))
        {
            ProjectSettings.Singleton.Disconnect(ProjectSettings.SignalName.SettingsChanged, settingsHook);
        }
    }

    private void StopPreviewServer()
    {
        previewServer?.Stop();
        previewServer = null;
    }
}
#endif
