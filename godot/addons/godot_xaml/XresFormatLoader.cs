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
    // Made once: a StringName made per call from a literal is left to its finalizer, which can run after the engine has shut
    // down (GXAML-81). A static one is still reachable at shutdown, when Godot's tracker disposes it.
    private static readonly StringName ResourceClass = "Resource";
    private static readonly StringName PackedSceneClass = "PackedScene";
    private static readonly StringName GDExtensionClass = "GDExtension";

    /// <summary>
    /// Rebuilds the global class snapshot, on the main thread only. Godot builds the loaders at startup from the previous
    /// session's class cache (<c>main/main.cpp:4494</c>) and re-creates them after each script-class update
    /// (<c>editor/file_system/editor_file_system.cpp:2195-2203</c>, <c>core/io/resource_loader.cpp:1684-1700</c>), so
    /// this covers a game run and an editor with the addon off; the editor plugin rebuilds the snapshot at editor start,
    /// after the first scan stores the fresh list, and on each <c>script_classes_updated</c>, which those re-creations miss.
    /// </summary>
    public XresFormatLoader()
    {
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            GlobalClassSnapshot.Rebuild();
        }
    }

    /// <summary>
    /// Rebuilds the loader's view of the project's global classes. Call it on the main thread after
    /// <c>ProjectSettings.LoadResourcePack</c> mounts a pack that declares new global classes: Godot refreshes its own list then
    /// (<c>core/config/project_settings.cpp:609-611</c>) but re-creates no loader and emits no signal, so without this call an
    /// <c>.xres</c> whose root script is one of the pack's classes reports no script class, and a load by that class name skips
    /// <c>.xres</c> files. Off the main thread it logs an error and changes nothing.
    /// </summary>
    public static void RefreshGlobalClasses() => GlobalClassSnapshot.Rebuild();

    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions() => [XscnLoading.ResourceExtension];

    /// <summary>
    /// Every resource type but <c>PackedScene</c> and <c>GDExtension</c>, as the text loader allows <c>.tres</c>, plus a
    /// global class whose native base is one of those types: the engine asks this when a caller passes the class as a type
    /// hint, as <c>ResourceLoader.Load(path, "MyRes")</c> and <c>ResourceLoader.Exists(path, "MyRes")</c> do.
    /// </summary>
    /// <param name="type">The class Godot asks about.</param>
    /// <returns>Whether an <c>.xres</c> can hold it.</returns>
    public override bool _HandlesType(StringName type)
    {
        if (ClassDB.IsParentClass(type, ResourceClass) && type != PackedSceneClass && type != GDExtensionClass)
        {
            return true;
        }

        string? native = GlobalClassSnapshot.Current.NativeBaseOf(type);
        if (native is null)
        {
            return false;
        }

        using var nativeName = new StringName(native);
        return ClassDB.IsParentClass(nativeName, ResourceClass) && native != "PackedScene" && native != "GDExtension";
    }

    /// <inheritdoc/>
    public override string _GetResourceType(string path) => XscnLoading.IsResource(path) ? XscnLoading.XresRootClass(path) : "";

    /// <summary>
    /// The global class name of the root's script, as a <c>.tres</c> header's <c>script_class</c> gives it, so a typed
    /// picker's Quick Load, an Array/Dictionary drop and the FileSystem dock know a script-class <c>.xres</c> by its class.
    /// </summary>
    /// <param name="path">The file's <c>res://</c> path.</param>
    /// <returns>The class name, or an empty string when the file is not an <c>.xres</c> or its root has no global-class script.</returns>
    public override string _GetResourceScriptClass(string path)
    {
        if (!XscnLoading.IsResource(path))
        {
            return "";
        }

        string? script = XscnLoading.XresRootScript(path);
        return script is null ? "" : GlobalClassSnapshot.Current.ClassOfScript(script) ?? "";
    }

    /// <inheritdoc/>
    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode) =>
        XscnLoading.Load(path, originalPath, cacheMode);

    /// <inheritdoc/>
    public override string[] _GetDependencies(string path, bool addTypes) => XscnLoading.Dependencies(path, addTypes);

    /// <inheritdoc/>
    public override Error _RenameDependencies(string path, Godot.Collections.Dictionary renames) => XscnLoading.RenameDependencies(path, renames);
}
