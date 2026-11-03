namespace GodotXaml.Core.Conversion;

/// <summary>A scene file a host read for the conversion: its identity and its text.</summary>
/// <param name="Key">
/// The host's identity for the file: the referrer of the references inside it, and its extension picks the parser
/// (<c>.xscn</c> the XML one, anything else the <c>.tscn</c> one).
/// </param>
/// <param name="Text">The file's text.</param>
public sealed record SceneFile(string Key, string Text);

/// <summary>
/// The scenes a conversion's document instances, read by a host that can open them. The way back reads the class of an
/// instanced node from its scene, since the property order Godot sets depends on it; the core opens no file itself.
/// </summary>
public interface ISceneFiles
{
    /// <summary>The scene a reference names.</summary>
    /// <param name="referrer">The <see cref="SceneFile.Key"/> of the file holding the reference.</param>
    /// <param name="reference">The scene's path as the file writes it: <c>res://</c>, or relative to the referrer.</param>
    /// <param name="uid">
    /// The <c>uid://</c> the referrer gives the reference (its <c>x:Uids</c> entry, or the <c>ext_resource</c>'s
    /// <c>uid</c>), which a host that knows the project's uids tries before the path; null when it gives none.
    /// </param>
    /// <returns>The file, or null when the host cannot give it.</returns>
    SceneFile? Read(string referrer, string reference, string? uid);
}
