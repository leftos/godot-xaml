using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Godot;
using Godot.Collections;
using GodotXaml.Core.Tscn;

namespace GodotXaml.Addon;

/// <summary>
/// Loads converted <c>.tscn</c> or <c>.tres</c> text through Godot's own text loader, which reads only files: the text
/// goes to a uniquely named file in the addon's per-user temp folder (<see cref="AddonTempFolder"/>), outside the project,
/// so no uid is minted for it.
/// </summary>
internal static class XscnLoadRoute
{
    // A kept file is never one a live load or save is using: each deletes its own file within the call that made it, so a
    // file this old is a leftover from a process that died.
    private static readonly TimeSpan KeptFileRetention = TimeSpan.FromHours(24);

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

    /// <summary>
    /// Writes the text to a temp file and loads it through Godot's text loader: with the cache ignored, or on a reuse or a
    /// replace in that mode, with the live sub-resources; then deletes the file unless the load failed, in which case it is
    /// kept and every kept file over a day old in the folder is removed, with the backup-save leftovers of one the save route's
    /// save through Godot can leave beside them.
    /// </summary>
    /// <param name="path">The <c>.xscn</c> or <c>.xres</c> being loaded, for the error message and its sub-resources' paths.</param>
    /// <param name="text">The converted text.</param>
    /// <param name="isScene">True for a scene (a <c>.tscn</c> temp file), false for a resource (a <c>.tres</c>).</param>
    /// <param name="typeHint">The class Godot is told to expect.</param>
    /// <param name="cacheMode">The cache mode Godot passed for the file.</param>
    /// <returns>The loaded resource, or <see cref="Error.FileCorrupt"/> when Godot could not load the text.</returns>
    public static Variant Load(string path, string text, bool isScene, string typeHint, ResourceFormatLoader.CacheMode cacheMode)
    {
        string folder = AddonTempFolder.Get();
        string tempPath = Path.Combine(folder, $"{Guid.NewGuid():N}.{(isScene ? "tscn" : "tres")}").Replace('\\', '/');
        if (RecordTempPaths)
        {
            TempPaths.Enqueue(tempPath);
        }

        bool keep = false;
        string tempPrefix = tempPath + "::";
        string pathPrefix = path + "::";
        var moved = new List<Resource>();
        try
        {
            File.WriteAllText(tempPath, text);
            if (cacheMode is ResourceFormatLoader.CacheMode.Replace or ResourceFormatLoader.CacheMode.ReplaceDeep)
            {
                MoveLiveSubResources(text, tempPrefix, pathPrefix, moved, sameClassOnly: true);
            }
            else if (cacheMode is ResourceFormatLoader.CacheMode.Reuse)
            {
                MoveLiveSubResources(text, tempPrefix, pathPrefix, moved, sameClassOnly: false);
            }

            Resource? loaded = ResourceLoader.Load(tempPath, typeHint, TextLoaderMode(cacheMode));
            if (loaded is null)
            {
                keep = true;
                PruneKeptFiles(folder, tempPath);
                GD.PushError($"{path}: Godot could not load the converted text; it is kept at {tempPath}.");
                return (int)Error.FileCorrupt;
            }

            bool cached = cacheMode is not (ResourceFormatLoader.CacheMode.Ignore or ResourceFormatLoader.CacheMode.IgnoreDeep);
            NameSubResourcesAfter(loaded, tempPrefix, pathPrefix, cached);
            Variant result = loaded;

            // The returned Variant holds its own reference, so releasing this wrapper lets the resource leave the cache once an
            // engine caller (the editor, GDScript) lets go of it. An object has one C# wrapper shared by every C# caller, and a
            // script's code may have handed it out while the file loaded, so it is released only for a resource with no script.
            using Variant script = loaded.GetScript();
            if (script.VariantType == Variant.Type.Nil)
            {
                loaded.Dispose();
            }

            return result;
        }
        finally
        {
            // Also after a failed load or a throw: every moved object goes home.
            MoveBack(moved, tempPrefix, pathPrefix);
            if (!keep)
            {
                File.Delete(tempPath);
            }
        }
    }

    // Deletes every *.tscn and *.tres file left in the folder more than a day ago, and every backup-save leftover of one, which
    // the save route leaves when Godot saves its temp file, never the file a failed load is keeping. A file that cannot be
    // removed, or a folder that cannot be listed, is a warning: nothing may throw out of the load path.
    private static void PruneKeptFiles(string folder, string keeping)
    {
        string kept = Path.GetFileName(keeping);
        DateTime cutoff = DateTime.UtcNow - KeptFileRetention;
        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"godot-xaml: could not list the kept files in {folder}: {exception.Message}");
            return;
        }

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (!IsOurs(name) || string.Equals(name, kept, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"godot-xaml: could not remove the old kept file {file}: {exception.Message}");
            }
        }
    }

    // A plain *.tscn or *.tres is taken by its extension alone, whoever wrote it. A suffixed name is taken only as a backup-save
    // leftover of one the routes write: "<32 lowercase hex>.tscn" or ".tres" gains "-XXXXXX", six characters from mkstemp
    // (drivers/unix/file_access_unix.cpp), or "<ticks>.tmp" (drivers/windows/file_access_windows.cpp). The 32-hex stem is
    // required of those suffixed names only, so another file's "anything.tscn-AbC123" is left alone, however old.
    private static bool IsOurs(string name)
    {
        if (Path.GetExtension(name) is ".tscn" or ".tres")
        {
            return true;
        }

        return HasOurStem(name.AsSpan(), out ReadOnlySpan<char> suffix) && (IsMkstempSuffix(suffix) || IsWindowsTempSuffix(suffix));
    }

    // Splits a name at its stem of ours and the ".tscn" or ".tres" after it, leaving what the backup save appended.
    private static bool HasOurStem(ReadOnlySpan<char> name, out ReadOnlySpan<char> suffix)
    {
        const int stemLength = 32;
        suffix = name;
        if (name.Length <= stemLength || !IsLowerHex(name[..stemLength]))
        {
            return false;
        }

        suffix = name[stemLength..];
        if (!(suffix.StartsWith(".tscn") || suffix.StartsWith(".tres")))
        {
            return false;
        }

        suffix = suffix[5..];
        return true;
    }

    // "<path>-XXXXXX", the six characters mkstemp replaced its template with.
    private static bool IsMkstempSuffix(ReadOnlySpan<char> suffix) => suffix.Length == 7 && suffix[0] == '-' && IsAsciiLetterOrDigit(suffix[1..]);

    // "<path><ticks>.tmp", one or more digits before the extension.
    private static bool IsWindowsTempSuffix(ReadOnlySpan<char> suffix)
    {
        int digits = 0;
        while (digits < suffix.Length && char.IsAsciiDigit(suffix[digits]))
        {
            digits++;
        }

        return digits > 0 && suffix[digits..].SequenceEqual(".tmp");
    }

    private static bool IsLowerHex(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
        {
            if (!char.IsAsciiHexDigitLower(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    // Only the reuse and replace modes reach the cache: they reuse the objects moved under the temp file's name.
    private static ResourceLoader.CacheMode TextLoaderMode(ResourceFormatLoader.CacheMode cacheMode) =>
        cacheMode switch
        {
            ResourceFormatLoader.CacheMode.Reuse => ResourceLoader.CacheMode.Reuse,
            ResourceFormatLoader.CacheMode.Replace => ResourceLoader.CacheMode.Replace,
            ResourceFormatLoader.CacheMode.ReplaceDeep => ResourceLoader.CacheMode.ReplaceDeep,
            ResourceFormatLoader.CacheMode.IgnoreDeep => ResourceLoader.CacheMode.IgnoreDeep,
            _ => ResourceLoader.CacheMode.Ignore,
        };

    // A replace reuses an object in place only when it finds it at the file's own "<file>::<id>", with the same class
    // (resource_format_text.cpp:576-583); a reuse takes whatever it finds there, with no class check and no change of path
    // (:589-592, 633). The file the text loader reads is the temp file, so each live sub-resource moves under the temp
    // file's name first, and the walk names it back.
    // Each object is listed as it moves, so a throw part way leaves the list naming every object to move back.
    private static void MoveLiveSubResources(string text, string tempPrefix, string pathPrefix, List<Resource> moved, bool sameClassOnly)
    {
        foreach (TscnSubResource sub in TscnReader.Read(text).SubResources)
        {
            if (ResourceLoader.GetCachedRef(pathPrefix + sub.Id) is { } live && (!sameClassOnly || live.GetClass() == sub.Type))
            {
                live.TakeOverPath(tempPrefix + sub.Id);
                moved.Add(live);
            }
        }
    }

    // Every moved object goes home: the walk names back only what the new scene reaches, and after a failed load nothing.
    private static void MoveBack(List<Resource> moved, string tempPrefix, string pathPrefix)
    {
        foreach (Resource resource in moved)
        {
            if (resource.ResourcePath.StartsWith(tempPrefix, StringComparison.Ordinal))
            {
                resource.TakeOverPath(pathPrefix + resource.ResourcePath[tempPrefix.Length..]);
            }
        }
    }

    /// <summary>Visits a resource and every resource it holds that the file owns, each once, the root first.</summary>
    /// <param name="root">The resource to start from; it is always visited.</param>
    /// <param name="isOwnPath">Whether a held resource with this path belongs to the file, so the walk enters it.</param>
    /// <param name="visit">Called with each resource entered, before the resources it holds are read.</param>
    internal static void WalkOwnResources(Resource root, Func<string, bool> isOwnPath, Action<Resource> visit)
    {
        var visited = new HashSet<ulong>();
        var pending = new Stack<Variant>([root]);
        // Each value is disposed once read: an undisposed Variant holds a reference that keeps its resource, and so the
        // loaded scene, in the cache until the finalizer runs. A sub-resource's C# wrapper made by the walk releases it at
        // GC, the same as any C# code that touches the object.
        while (pending.TryPop(out Variant value))
        {
            using (value)
            {
                Resource? resource = value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Resource : null;
                if (resource is null)
                {
                    PushContainerItems(value, pending);
                }
                else if (
                    visited.Add(resource.GetInstanceId()) && (resource.GetInstanceId() == root.GetInstanceId() || isOwnPath(resource.ResourcePath))
                )
                {
                    visit(resource);
                    PushHeldValues(resource, pending);
                }
            }
        }
    }

    // The text loader names each sub-resource "<file>::<id>" after the temp file; Godot's own load of a .tscn names it
    // after the scene (resource_format_text.cpp:632-636): into the cache unless the cache is ignored. TakeOverPath stands in
    // place of SetPathCache, not after it, since set_path returns early on an equal path (resource.cpp:80-82). The walk
    // enters only the loaded resource and resources with no path or a temp-file path: another file cannot hold this file's
    // sub-resources, and reading its properties can be costly (a mesh's buffers). Each one named is recorded in
    // SubResourceOrigins with this file and its id, in one call for the load, since a later save of another file may take
    // its path over.
    private static void NameSubResourcesAfter(Resource loaded, string tempPrefix, string pathPrefix, bool cached)
    {
        var origins = new List<(ulong InstanceId, string Id)>();
        WalkOwnResources(
            loaded,
            resourcePath => IsOwnSubResource(resourcePath, tempPrefix),
            resource =>
            {
                Interlocked.Increment(ref walkVisits);
                if (resource.ResourcePath.StartsWith(tempPrefix, StringComparison.Ordinal))
                {
                    string id = resource.ResourcePath[tempPrefix.Length..];
                    string named = pathPrefix + id;
                    origins.Add((resource.GetInstanceId(), id));
                    if (cached)
                    {
                        resource.TakeOverPath(named);
                    }
                    else
                    {
                        resource.SetPathCache(named);
                    }
                }
            }
        );
        SubResourceOrigins.RecordAll(ProjectSettings.LocalizePath(pathPrefix[..^2]), origins);
    }

    private static bool IsOwnSubResource(string resourcePath, string tempPrefix) =>
        resourcePath.Length == 0 || resourcePath.StartsWith(tempPrefix, StringComparison.Ordinal);

    // A packed scene's values are walked through its state; its _bundled property holds the same values again. Only stored
    // properties are read, as Godot's text saver reads them (resource_format_text.cpp:1690): a script variable that is not
    // exported holds what another file owns.
    // Every Godot value read here is disposed, the literal keys and the list itself included: one left to its finalizer can
    // be destroyed after the engine has shut down (GXAML-81). The typed list's cast returns its untyped Array, which can be.
    private static void PushHeldValues(Resource resource, Stack<Variant> pending)
    {
        var scene = resource as PackedScene;
        if (scene is not null)
        {
            PushSceneValues(scene.GetState(), pending);
        }

        using Variant nameKey = "name";
        using Variant usageKey = "usage";
        using var properties = (Godot.Collections.Array)resource.GetPropertyList();
        foreach (Variant item in properties)
        {
            using (item)
            {
                using Dictionary property = item.AsGodotDictionary();
                using Variant nameValue = property[nameKey];
                using Variant usage = property[usageKey];
                string name = nameValue.AsString();
                if ((usage.AsInt64() & (long)PropertyUsageFlags.Storage) != 0 && (scene is null || name != "_bundled"))
                {
                    using var key = new StringName(name);
                    // The walk owns the pushed value and disposes it once read.
                    pending.Push(resource.Get(key));
                }
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
            using Godot.Collections.Array binds = state.GetConnectionBinds(connection);
            PushArrayItems(binds, pending);
        }
    }

    // A container wrapper holds its native container, and so every resource in it, until it is disposed: each one's items
    // are copied out and the wrapper let go at once.
    private static void PushContainerItems(Variant value, Stack<Variant> pending)
    {
        if (value.VariantType == Variant.Type.Array)
        {
            using Godot.Collections.Array array = value.AsGodotArray();
            PushArrayItems(array, pending);
        }
        else if (value.VariantType == Variant.Type.Dictionary)
        {
            using Dictionary dictionary = value.AsGodotDictionary();
            foreach (KeyValuePair<Variant, Variant> pair in dictionary)
            {
                pending.Push(pair.Key);
                pending.Push(pair.Value);
            }
        }
    }

    private static void PushArrayItems(Godot.Collections.Array array, Stack<Variant> pending)
    {
        foreach (Variant item in array)
        {
            pending.Push(item);
        }
    }
}
