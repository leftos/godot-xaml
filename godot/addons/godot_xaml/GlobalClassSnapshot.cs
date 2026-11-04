using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// An immutable view of the project's global classes, built on the main thread and readable from any thread. The editor
/// asks a loader for a file's script class from its scan thread (<c>editor/file_system/editor_file_system.cpp:1144-1147,
/// 1299, 1357</c>), where the live list cannot be read: the main thread replaces it with no lock
/// (<c>core/config/project_settings.cpp:1446-1480</c>). Godot builds the loaders at startup from the previous session's
/// class cache (<c>main/main.cpp:4494</c>) and re-creates them after each script-class update
/// (<c>editor/file_system/editor_file_system.cpp:2195-2203</c>), so <see cref="XresFormatLoader"/>'s constructor and
/// <c>XscnEditorPlugin</c> keep this snapshot current: the plugin rebuilds it when it enters the tree, after the
/// first scan stored the fresh list (<c>editor/file_system/editor_file_system.cpp:336-343, 360</c>), and on each
/// <c>script_classes_updated</c>. A game that mounts a pack calls <see cref="XresFormatLoader.RefreshGlobalClasses"/>, since
/// mounting refreshes Godot's list with neither.
/// </summary>
internal sealed class GlobalClassSnapshot
{
    private static readonly GlobalClassSnapshot Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal)
    );

    private static GlobalClassSnapshot? latest;

    private readonly FrozenDictionary<string, string> classOfPath;
    private readonly FrozenDictionary<string, string> baseOfClass;

    private GlobalClassSnapshot(FrozenDictionary<string, string> classOfPath, FrozenDictionary<string, string> baseOfClass)
    {
        this.classOfPath = classOfPath;
        this.baseOfClass = baseOfClass;
    }

    /// <summary>
    /// The latest snapshot, read with one <c>Volatile.Read</c> per call so a reader works on a single immutable object
    /// while the main thread publishes the next one. When none has been built yet, the main thread builds one and returns
    /// it; any other thread gets an empty snapshot, since the live list may be read on the main thread only.
    /// </summary>
    public static GlobalClassSnapshot Current
    {
        get
        {
            GlobalClassSnapshot? snapshot = Volatile.Read(ref latest);
            if (snapshot is not null)
            {
                return snapshot;
            }

            if (!OnMainThread())
            {
                return Empty;
            }

            Rebuild();
            return Volatile.Read(ref latest)!;
        }
    }

    /// <summary>
    /// Rebuilds the snapshot from the live global class list and publishes it in one write. It runs on the main thread
    /// only, where the list is replaced with no lock; off it, the published snapshot is left as it is.
    /// </summary>
    public static void Rebuild()
    {
        if (!OnMainThread())
        {
            GD.PushError("godot-xaml: the global class snapshot can only be rebuilt on the main thread.");
            return;
        }

        Volatile.Write(ref latest, Build());
    }

    /// <summary>The global class name the script at a path declares, as the project's own list records it.</summary>
    /// <param name="scriptPath">The script's <c>res://</c> path.</param>
    /// <returns>The class name, or null when the script declares none.</returns>
    public string? ClassOfScript(string scriptPath) => classOfPath.GetValueOrDefault(scriptPath);

    /// <summary>
    /// The engine class a global class ultimately derives from, as <c>ScriptServer::get_global_class_native_base</c> walks
    /// it (<c>core/object/script_language.cpp:502-509</c>): the first base name that is not itself a global class.
    /// </summary>
    /// <param name="className">The class name Godot asked about.</param>
    /// <returns>The native base's name, or null when the name is not a global class or its bases form a cycle.</returns>
    public string? NativeBaseOf(string className)
    {
        if (!baseOfClass.TryGetValue(className, out string? current))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { className };
        while (baseOfClass.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // The list, each of its items, each class's Godot Dictionary, the keys and the values are all owned by the caller, so
    // each is disposed as it is read: one left to its finalizer can be destroyed after the engine has shut down (GXAML-81).
    // The typed array wrapper has no Dispose of its own, but casting it to Godot.Collections.Array returns the untyped
    // Array it wraps (modules/mono/glue/GodotSharp/GodotSharp/Core/Array.cs:1205-1208), so disposing the cast releases it.
    private static GlobalClassSnapshot Build()
    {
        var classOfPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var baseOfClass = new Dictionary<string, string>(StringComparer.Ordinal);
        using Variant classKey = "class";
        using Variant pathKey = "path";
        using Variant baseKey = "base";
        using var list = (Godot.Collections.Array)ProjectSettings.GetGlobalClassList();
        foreach (Variant item in list)
        {
            using (item)
            {
                using Godot.Collections.Dictionary entry = item.AsGodotDictionary();
                using Variant className = entry[classKey];
                using Variant path = entry[pathKey];
                using Variant baseName = entry[baseKey];
                string name = className.AsString();
                classOfPath[path.AsString()] = name;
                baseOfClass[name] = baseName.AsString();
            }
        }

        return new GlobalClassSnapshot(
            classOfPath.ToFrozenDictionary(StringComparer.Ordinal),
            baseOfClass.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }

    private static bool OnMainThread() => OS.GetThreadCallerId() == OS.GetMainThreadId();
}
