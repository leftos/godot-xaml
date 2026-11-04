namespace GodotXaml.Core.Conversion;

/// <summary>A file a host read for the conversion: its identity and its text.</summary>
/// <param name="Key">
/// The host's identity for the file: the referrer of the references inside it. For an instanced scene its extension picks
/// the parser (<c>.xscn</c> the XML one, anything else the <c>.tscn</c> one).
/// </param>
/// <param name="Text">The file's text.</param>
public sealed record SceneFile(string Key, string Text);

/// <summary>
/// The files a conversion's document refers to, read by a host that can open them, since the property order Godot sets
/// depends on them; the core opens no file itself. The way back reads the class of an instanced node from its scene, and
/// the order of a by-source family from a shader's text (a <c>.gdshader</c>, for a ShaderMaterial's parameters) or a
/// mesh's text (a <c>.tres</c> ArrayMesh, for a MeshInstance3D's blend shapes). A host reads any extension it is asked for.
/// </summary>
public interface ISceneFiles
{
    /// <summary>The file a reference names.</summary>
    /// <param name="referrer">The <see cref="SceneFile.Key"/> of the file holding the reference.</param>
    /// <param name="reference">The file's path as the referrer writes it: <c>res://</c>, or relative to the referrer.</param>
    /// <param name="uid">
    /// The <c>uid://</c> the referrer gives the reference (its <c>x:Uids</c> entry, or the <c>ext_resource</c>'s
    /// <c>uid</c>), which a host that knows the project's uids tries before the path; null when it gives none.
    /// </param>
    /// <returns>The file, or null when the host cannot give it.</returns>
    SceneFile? Read(string referrer, string reference, string? uid);
}
