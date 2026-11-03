using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotXaml.Addon;

/// <summary>
/// The file each live sub-resource was loaded from and the id it had there. A save that takes a borrowed sub-resource
/// over renames its path, as Godot's text saver does, so the path alone no longer says which file the sub-resource came
/// from; this table does. Only a load writes an origin; a save reads it and never changes it.
/// </summary>
internal static class SubResourceOrigins
{
    // Keyed by instance id, not by the C# wrapper: the load route disposes wrappers, and a freed object's id is never valid
    // again. Loads may run on loader threads, so every access holds the lock.
    private static readonly Dictionary<ulong, (string File, string Id)> Origins = [];

    private static readonly object Gate = new();

    /// <summary>
    /// Records that each of one load's sub-resources was loaded from the file with its id; first drops the entries of freed
    /// objects, once for the whole load.
    /// </summary>
    /// <param name="file">The file they were loaded from, in localized <c>res://</c> form.</param>
    /// <param name="loaded">Each sub-resource's instance id and the id it had in that file.</param>
    public static void RecordAll(string file, IReadOnlyList<(ulong InstanceId, string Id)> loaded)
    {
        if (loaded.Count == 0)
        {
            return;
        }

        lock (Gate)
        {
            foreach (ulong freed in Origins.Keys.Where(key => !GodotObject.IsInstanceIdValid(key)).ToList())
            {
                Origins.Remove(freed);
            }

            foreach ((ulong instanceId, string id) in loaded)
            {
                Origins[instanceId] = (file, id);
            }
        }
    }

    /// <summary>The file the sub-resource was loaded from and the id it had there, if a load recorded one.</summary>
    /// <param name="resource">The live sub-resource.</param>
    /// <param name="origin">The recorded file, in localized <c>res://</c> form, and id.</param>
    /// <returns>True when a load recorded the resource's origin.</returns>
    public static bool TryGet(Resource resource, out (string File, string Id) origin)
    {
        ulong instanceId = resource.GetInstanceId();
        lock (Gate)
        {
            return Origins.TryGetValue(instanceId, out origin) && GodotObject.IsInstanceIdValid(instanceId);
        }
    }
}
