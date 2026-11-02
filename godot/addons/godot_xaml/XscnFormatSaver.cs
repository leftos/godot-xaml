using System.Threading;
using Godot;

namespace GodotXaml.Addon;

/// <summary>Godot's saver for <c>.xscn</c> scenes and <c>.xres</c> resources, registered as a global class.</summary>
[GlobalClass]
[Tool]
public partial class XscnFormatSaver : ResourceFormatSaver
{
    private static int saveCallCount;

    /// <summary>How many times Godot has called <see cref="_Save"/> since the assembly loaded.</summary>
    internal static int SaveCallCount => Volatile.Read(ref saveCallCount);

    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions(Resource resource) => resource is PackedScene ? ["xscn"] : ["xres"];

    /// <inheritdoc/>
    public override bool _Recognize(Resource resource) => resource is not null;

    /// <inheritdoc/>
    public override Error _Save(Resource resource, string path, uint flags)
    {
        Interlocked.Increment(ref saveCallCount);
        GD.PushError($"{path}: godot-xaml cannot save yet; the .xscn writer is not built.");
        return Error.Unavailable;
    }
}
