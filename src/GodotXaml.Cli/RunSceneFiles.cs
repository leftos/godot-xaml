using GodotXaml.Core.Conversion;
using GodotXaml.Core.Projects;

namespace GodotXaml.Cli;

/// <summary>
/// The files a convert run's way back reads, keyed by full OS path: a scene for the class of an instanced node, and a
/// shader's or a mesh's file for the order of a material's parameters or a node's blend shapes. A reference to a
/// file this run converts reads that file's source, from memory: on a <c>--back</c> run a rewritten reference names an
/// output that is not on disk yet. Only the sources the run converts stand in for their outputs; the output of a refused
/// or failed source is read from disk. Any other file is read from disk once per run. The uid is not used: a run's paths
/// are current.
/// </summary>
internal sealed class RunSceneFiles : ISceneFiles
{
    private const string ResScheme = "res://";

    private readonly Dictionary<string, SceneFile> byOutput = new(InputFiles.PathComparer);
    private readonly Dictionary<string, SceneFile> bySource = new(InputFiles.PathComparer);
    private readonly Dictionary<string, SceneFile?> disk = new(InputFiles.PathComparer);

    /// <summary>Makes the reader over a run's sources, none of them yet standing in for its output.</summary>
    /// <param name="sources">Each source's path and its text.</param>
    public RunSceneFiles(IEnumerable<(string Path, string Text)> sources)
    {
        foreach ((string path, string text) in sources)
        {
            var file = new SceneFile(Path.GetFullPath(path), text);
            bySource.TryAdd(file.Key, file);
        }
    }

    /// <summary>Sets the sources whose outputs the run writes, each of which then stands in for its output.</summary>
    /// <param name="converted">Each such source's path and the path of its output.</param>
    public void Converting(IEnumerable<(string Path, string Output)> converted)
    {
        byOutput.Clear();
        foreach ((string path, string output) in converted)
        {
            if (bySource.TryGetValue(Path.GetFullPath(path), out SceneFile? file))
            {
                byOutput.TryAdd(Path.GetFullPath(output), file);
            }
        }
    }

    /// <inheritdoc/>
    public SceneFile? Read(string referrer, string reference, string? uid)
    {
        string? full = FullPath(referrer, reference);
        if (full is null)
        {
            return null;
        }

        if (byOutput.TryGetValue(full, out SceneFile? converted) || bySource.TryGetValue(full, out converted))
        {
            return converted;
        }

        if (!disk.TryGetValue(full, out SceneFile? read))
        {
            read = FromDisk(full);
            disk.Add(full, read);
        }

        return read;
    }

    // A res:// path lies under the referrer's project root, a relative one beside the referrer; another scheme has no file.
    private static string? FullPath(string referrer, string reference)
    {
        if (reference.StartsWith(ResScheme, StringComparison.Ordinal))
        {
            string? root = GodotProject.Find(referrer);
            string relative = reference[ResScheme.Length..].Replace('/', Path.DirectorySeparatorChar);
            return root is null ? null : Path.GetFullPath(Path.Combine(root, relative));
        }

        if (reference.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        string folder = Path.GetDirectoryName(referrer) ?? "";
        return Path.GetFullPath(Path.Combine(folder, reference.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static SceneFile? FromDisk(string full)
    {
        try
        {
            return new SceneFile(full, File.ReadAllText(full));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
