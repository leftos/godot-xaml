using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// Godot's loader for <c>.xscn</c> scenes, registered as a global class. It handles only <see cref="PackedScene"/> and
/// its ancestors, so a picker for another resource type does not list <c>.xscn</c> files.
/// </summary>
[GlobalClass]
[Tool]
public partial class XscnFormatLoader : ResourceFormatLoader
{
    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions() => [XscnLoading.SceneExtension];

    /// <inheritdoc/>
    public override bool _HandlesType(StringName type) => ClassDB.IsParentClass("PackedScene", type);

    /// <inheritdoc/>
    public override string _GetResourceType(string path) => XscnLoading.IsScene(path) ? "PackedScene" : "";

    /// <inheritdoc/>
    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode) =>
        XscnLoading.Load(path, originalPath, cacheMode);

    /// <inheritdoc/>
    public override string[] _GetDependencies(string path, bool addTypes) => XscnLoading.Dependencies(path, addTypes);

    /// <inheritdoc/>
    public override Error _RenameDependencies(string path, Godot.Collections.Dictionary renames) => XscnLoading.RenameDependencies(path, renames);
}
