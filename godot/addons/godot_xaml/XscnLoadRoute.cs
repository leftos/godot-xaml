using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Godot;
using Godot.Collections;

namespace GodotXaml.Addon;

/// <summary>
/// Loads converted <c>.tscn</c> or <c>.tres</c> text through Godot's own text loader, which reads only files: the text
/// goes to a uniquely named file in the OS temp folder, outside the project, so no uid is minted for it.
/// </summary>
internal static class XscnLoadRoute
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "godot-xaml");

    private static int walkVisits;

    private static volatile bool recordTempPaths;

    /// <summary>Whether <see cref="TempPaths"/> records each temp file path; off unless the checks turn it on.</summary>
    internal static bool RecordTempPaths
    {
        get => recordTempPaths;
        set => recordTempPaths = value;
    }

    /// <summary>Every temp file path this route has created while <see cref="RecordTempPaths"/> was on, in order.</summary>
    internal static ConcurrentQueue<string> TempPaths { get; } = new();

    /// <summary>How many resources the sub-resource walk has entered since the last <see cref="ResetWalkVisits"/>.</summary>
    internal static int WalkVisits => Volatile.Read(ref walkVisits);

    /// <summary>Sets <see cref="WalkVisits"/> back to zero.</summary>
    internal static void ResetWalkVisits() => Interlocked.Exchange(ref walkVisits, 0);

    /// <summary>Writes the text to a temp file, loads it with the cache ignored, and deletes the file unless the load failed.</summary>
    /// <param name="path">The <c>.xscn</c> or <c>.xres</c> being loaded, for the error message and its sub-resources' paths.</param>
    /// <param name="text">The converted text.</param>
    /// <param name="isScene">True for a scene (a <c>.tscn</c> temp file), false for a resource (a <c>.tres</c>).</param>
    /// <param name="typeHint">The class Godot is told to expect.</param>
    /// <param name="deep">True to ignore the cache for the file's dependencies too, as the caller's deep cache mode asks.</param>
    /// <returns>The loaded resource, or <see cref="Error.FileCorrupt"/> when Godot could not load the text.</returns>
    public static Variant Load(string path, string text, bool isScene, string typeHint, bool deep)
    {
        Directory.CreateDirectory(Folder);
        string tempPath = Path.Combine(Folder, $"{Guid.NewGuid():N}.{(isScene ? "tscn" : "tres")}").Replace('\\', '/');
        if (RecordTempPaths)
        {
            TempPaths.Enqueue(tempPath);
        }

        bool keep = false;
        try
        {
            File.WriteAllText(tempPath, text);
            ResourceLoader.CacheMode mode = deep ? ResourceLoader.CacheMode.IgnoreDeep : ResourceLoader.CacheMode.Ignore;
            Resource? loaded = ResourceLoader.Load(tempPath, typeHint, mode);
            if (loaded is null)
            {
                keep = true;
                GD.PushError($"{path}: Godot could not load the converted text; it is kept at {tempPath}.");
                return (int)Error.FileCorrupt;
            }

            NameSubResourcesAfter(loaded, tempPath + "::", path + "::");
            return loaded;
        }
        finally
        {
            if (!keep)
            {
                File.Delete(tempPath);
            }
        }
    }

    // The text loader names each sub-resource "<file>::<id>" after the temp file; Godot's own load of a .tscn names it
    // after the scene (resource_format_text.cpp:632-636). Renaming with SetPathCache, as the text loader does, keeps the
    // resource out of the cache. The walk enters only the loaded resource and resources with no path or a temp-file path:
    // another file cannot hold this file's sub-resources, and reading its properties can be costly (a mesh's buffers).
    private static void NameSubResourcesAfter(Resource loaded, string tempPrefix, string pathPrefix)
    {
        var visited = new HashSet<ulong>();
        var pending = new Stack<Variant>([loaded]);
        while (pending.TryPop(out Variant value))
        {
            Resource? resource = value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Resource : null;
            if (resource is null)
            {
                PushContainerItems(value, pending);
            }
            else if (
                visited.Add(resource.GetInstanceId())
                && (resource.GetInstanceId() == loaded.GetInstanceId() || IsOwnSubResource(resource.ResourcePath, tempPrefix))
            )
            {
                Interlocked.Increment(ref walkVisits);
                if (resource.ResourcePath.StartsWith(tempPrefix, StringComparison.Ordinal))
                {
                    resource.SetPathCache(pathPrefix + resource.ResourcePath[tempPrefix.Length..]);
                }

                PushHeldValues(resource, pending);
            }
        }
    }

    private static bool IsOwnSubResource(string resourcePath, string tempPrefix) =>
        resourcePath.Length == 0 || resourcePath.StartsWith(tempPrefix, StringComparison.Ordinal);

    // A packed scene's values are walked through its state; its _bundled property holds the same values again.
    private static void PushHeldValues(Resource resource, Stack<Variant> pending)
    {
        var scene = resource as PackedScene;
        if (scene is not null)
        {
            PushSceneValues(scene.GetState(), pending);
        }

        foreach (Dictionary property in resource.GetPropertyList())
        {
            StringName name = property["name"].AsStringName();
            if (scene is null || name != "_bundled")
            {
                pending.Push(resource.Get(name));
            }
        }
    }

    private static void PushSceneValues(SceneState state, Stack<Variant> pending)
    {
        for (int node = 0; node < state.GetNodeCount(); node++)
        {
            for (int property = 0; property < state.GetNodePropertyCount(node); property++)
            {
                pending.Push(state.GetNodePropertyValue(node, property));
            }
        }

        for (int connection = 0; connection < state.GetConnectionCount(); connection++)
        {
            pending.Push(state.GetConnectionBinds(connection));
        }
    }

    private static void PushContainerItems(Variant value, Stack<Variant> pending)
    {
        if (value.VariantType == Variant.Type.Array)
        {
            foreach (Variant item in value.AsGodotArray())
            {
                pending.Push(item);
            }
        }
        else if (value.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> pair in value.AsGodotDictionary())
            {
                pending.Push(pair.Key);
                pending.Push(pair.Value);
            }
        }
    }
}
