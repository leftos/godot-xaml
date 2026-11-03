using Godot;
using GodotXaml.Core.Conversion;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>
/// The scenes a load's conversion reads for the class of an instanced node, keyed by <c>res://</c> path. A known uid
/// names the file before the path, as Godot's text loader resolves an ext_resource (resource_format_text.cpp:491-496).
/// It reads the text with <c>FileAccess</c>, whose open error is per thread (core/io/file_access.h:146) and which is
/// silent on a missing file when asked for the error (file_access.cpp:922-927), so loader threads can read at once; it
/// never asks <c>ResourceLoader</c>, which could load the scene again.
/// </summary>
internal sealed class GodotSceneFiles : ISceneFiles
{
    /// <summary>The one instance; it holds no state.</summary>
    public static GodotSceneFiles Instance { get; } = new();

    /// <inheritdoc/>
    public SceneFile? Read(string referrer, string reference, string? uid)
    {
        string path = PathOfUid(uid) ?? XscnLoading.Localized(referrer, reference);
        string text = FileAccess.GetFileAsString(path);
        return FileAccess.GetOpenError() == Error.Ok ? new SceneFile(path, text) : null;
    }

    private static string? PathOfUid(string? uid)
    {
        if (uid is null)
        {
            return null;
        }

        long id = ResourceUid.TextToId(uid);
        return id != ResourceUid.InvalidId && ResourceUid.HasId(id) ? ResourceUid.GetIdPath(id) : null;
    }
}
