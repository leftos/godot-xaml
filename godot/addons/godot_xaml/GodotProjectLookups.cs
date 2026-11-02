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
    /// The class the editor's filesystem records for the file. Outside the editor (a game run, an exported game) there is
    /// none, and the conversion falls back to the property's declared type, then to the file's extension.
    /// </summary>
    /// <param name="path">The file's <c>res://</c> path.</param>
    /// <returns>The class name, or null when it is not known.</returns>
    public string? ClassOf(string path)
    {
#if TOOLS
        if (Engine.IsEditorHint())
        {
            string type = EditorInterface.Singleton.GetResourceFilesystem().GetFileType(path);
            return type.Length > 0 ? type : null;
        }
#endif
        return null;
    }

    /// <summary>The global class name (<c>class_name</c>) of the script at a path, from the project's global class list.</summary>
    /// <param name="scriptPath">The script's <c>res://</c> path.</param>
    /// <returns>The class name, or null when the script declares none.</returns>
    public static string? ScriptClassOf(string scriptPath)
    {
        foreach (Dictionary entry in ProjectSettings.GetGlobalClassList())
        {
            if (entry["path"].AsString() == scriptPath)
            {
                return entry["class"].AsString();
            }
        }

        return null;
    }
}
