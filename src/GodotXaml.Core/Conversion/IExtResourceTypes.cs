namespace GodotXaml.Core.Conversion;

/// <summary>
/// The class an <c>[ext_resource]</c>'s <c>type=</c> names for a file, which an .xscn does not record. A host that can
/// read the project (a <c>.tres</c> header, a <c>.import</c> file) knows it better than a guess by extension.
/// </summary>
public interface IExtResourceTypes
{
    /// <summary>The class Godot saves the file's resource as (<c>CompressedTexture2D</c>, <c>Material</c>).</summary>
    /// <param name="path">The file's <c>res://</c> path.</param>
    /// <returns>The class name, or null when the host does not know it.</returns>
    string? ClassOf(string path);
}
