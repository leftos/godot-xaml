using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// Godot's loader for <c>.xres</c> resources, registered as a global class. It handles every resource type but
/// <see cref="PackedScene"/>, so Scene > Open Scene does not list <c>.xres</c> files.
/// </summary>
[GlobalClass]
[Tool]
public partial class XresFormatLoader : ResourceFormatLoader
{
    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions() => [XscnLoading.ResourceExtension];

    /// <summary>Every resource type but <c>PackedScene</c> and <c>GDExtension</c>, as the text loader allows <c>.tres</c>.</summary>
    /// <param name="type">The class Godot asks about.</param>
    /// <returns>Whether an <c>.xres</c> can hold it.</returns>
    public override bool _HandlesType(StringName type) => ClassDB.IsParentClass(type, "Resource") && type != "PackedScene" && type != "GDExtension";

    /// <inheritdoc/>
    public override string _GetResourceType(string path) => XscnLoading.IsResource(path) ? XscnLoading.XresRootClass(path) : "";

    /// <inheritdoc/>
    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode) =>
        XscnLoading.Load(path, originalPath, cacheMode);

    /// <inheritdoc/>
    public override string[] _GetDependencies(string path, bool addTypes) => XscnLoading.Dependencies(path, addTypes);

    /// <inheritdoc/>
    public override Error _RenameDependencies(string path, Godot.Collections.Dictionary renames) => XscnLoading.RenameDependencies(path, renames);
}
