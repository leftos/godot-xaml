using System;
using System.IO;
using System.Threading;
using Godot;

namespace GodotXaml.Addon;

/// <summary>Godot's saver for <c>.xscn</c> scenes and <c>.xres</c> resources, registered as a global class.</summary>
[GlobalClass]
[Tool]
public partial class XscnFormatSaver : ResourceFormatSaver
{
    private const string SceneExtension = "xscn";
    private const string ResourceExtension = "xres";

    private static int saveCallCount;

    /// <summary>How many times Godot has called <see cref="_Save"/> since the assembly loaded.</summary>
    internal static int SaveCallCount => Volatile.Read(ref saveCallCount);

    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions(Resource resource) => resource is PackedScene ? [SceneExtension] : [ResourceExtension];

    /// <summary>True for every resource but a script, so the editor's Save As for a script offers no <c>.xres</c>.</summary>
    /// <param name="resource">The resource being saved.</param>
    /// <returns>Whether this saver can save it.</returns>
    public override bool _Recognize(Resource resource) => resource is not null and not Script;

    /// <inheritdoc/>
    public override Error _Save(Resource resource, string path, uint flags)
    {
        Interlocked.Increment(ref saveCallCount);
        try
        {
            return XscnSaveRoute.Save(resource, path, flags);
        }
        // Godot reads an exception escaping a virtual as a nil return, which is Error.OK: the resource would be marked
        // saved while the file is unchanged.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Failed(path, exception);
        }
    }

    /// <summary>Writes the file's <c>.uid</c> sidecar, where an <c>.xscn</c> or <c>.xres</c> keeps its uid.</summary>
    /// <param name="path">The file; only an <c>.xscn</c> or <c>.xres</c> is this saver's.</param>
    /// <param name="uid">The uid.</param>
    /// <returns>
    /// <see cref="Error.Ok"/>; <see cref="Error.FileUnrecognized"/> for another extension, so Godot asks the next saver;
    /// <see cref="Error.FileCantWrite"/> when the sidecar could not be written.
    /// </returns>
    public override Error _SetUid(string path, long uid)
    {
        try
        {
            string extension = path.GetExtension();
            bool ours =
                extension.Equals(SceneExtension, StringComparison.OrdinalIgnoreCase)
                || extension.Equals(ResourceExtension, StringComparison.OrdinalIgnoreCase);
            return ours ? WriteSidecar(path, uid) : Error.FileUnrecognized;
        }
        // Godot reads an exception escaping a virtual as a nil return, which is Error.OK.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Failed(path, exception);
        }
    }

    private static Error WriteSidecar(string path, long uid)
    {
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path) + ".uid", ResourceUid.IdToText(uid) + "\n");
            return Error.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"{path}: godot-xaml cannot write the .uid sidecar: {exception.Message}");
            return Error.FileCantWrite;
        }
    }

    private static Error Failed(string path, Exception exception)
    {
        GD.PushError($"{path}: godot-xaml: the save failed: {exception}");
        return Error.Failed;
    }
}
