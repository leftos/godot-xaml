#if TOOLS
using Godot;

namespace GodotXaml.Addon;

/// <summary>The addon's editor plugin; it registers the export plugin and nothing else.</summary>
/// <remarks>The loader and saver register as global classes, never from here.</remarks>
[Tool]
public partial class XscnEditorPlugin : EditorPlugin
{
    private XscnExportPlugin? exportPlugin;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        exportPlugin = new XscnExportPlugin();
        AddExportPlugin(exportPlugin);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (exportPlugin is null)
        {
            return;
        }

        RemoveExportPlugin(exportPlugin);
        exportPlugin = null;
    }
}
#endif
