namespace GodotXaml.Core.Projects;

/// <summary>The Godot project a file belongs to: the nearest folder at or above it holding <c>project.godot</c>.</summary>
public static class GodotProject
{
    private const string FileName = "project.godot";

    /// <summary>Finds the project root of a file.</summary>
    /// <param name="fileFullPath">The file's full path.</param>
    /// <returns>The nearest root folder's full path, or null when no folder at or above the file holds <c>project.godot</c>.</returns>
    public static string? Find(string fileFullPath)
    {
        for (DirectoryInfo? folder = new(Path.GetDirectoryName(Path.GetFullPath(fileFullPath))!); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, FileName)))
            {
                return folder.FullName;
            }
        }
        return null;
    }

    /// <summary>A file's <c>res://</c> path under a root, each segment in the casing it has on disk.</summary>
    /// <param name="root">The project root's full path.</param>
    /// <param name="fileFullPath">The file's full path.</param>
    /// <returns>The <c>res://</c> path, with '/' as the separator whatever the OS uses.</returns>
    public static string ResPath(string root, string fileFullPath) =>
        ResPath(root, fileFullPath, new Dictionary<string, string[]>(StringComparer.Ordinal));

    /// <summary>A file's <c>res://</c> path under a root, each folder listed once across the calls that share the listings.</summary>
    /// <param name="root">The project root's full path.</param>
    /// <param name="fileFullPath">The file's full path.</param>
    /// <param name="listings">Each folder's entries so far; shared only while the disk does not change.</param>
    /// <returns>The <c>res://</c> path, with '/' as the separator whatever the OS uses.</returns>
    public static string ResPath(string root, string fileFullPath, Dictionary<string, string[]> listings)
    {
        string[] segments = Path.GetRelativePath(root, Path.GetFullPath(fileFullPath))
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        string folder = root;
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = OnDiskName(folder, segments[index], listings);
            folder = Path.Combine(folder, segments[index]);
        }
        return "res://" + string.Join('/', segments);
    }

    /// <summary>
    /// The full path of a <c>res://</c> path under a root, the inverse of <see cref="ResPath(string, string)"/>: each
    /// segment that exists in the casing it has on disk, each one that does not kept as written.
    /// </summary>
    /// <param name="root">The project root's full path.</param>
    /// <param name="resPath">The <c>res://</c> path, with '/' as the separator.</param>
    /// <returns>The full path under <paramref name="root"/>; <paramref name="root"/> itself for <c>res://</c> alone.</returns>
    /// <exception cref="ArgumentException"><paramref name="resPath"/> does not start with <c>res://</c>.</exception>
    public static string DiskPath(string root, string resPath)
    {
        const string Scheme = "res://";
        if (!resPath.StartsWith(Scheme, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{resPath}' is not a res:// path.", nameof(resPath));
        }

        Dictionary<string, string[]> listings = new(StringComparer.Ordinal);
        string path = root;
        foreach (string segment in resPath[Scheme.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, OnDiskName(path, segment, listings));
        }
        return path;
    }

    // The name a folder's entry has on disk, so a path typed in another case still names the file Godot loads. A
    // folder that cannot be listed, and a name with no entry yet such as an output, keeps the name it was given.
    private static string OnDiskName(string folder, string name, Dictionary<string, string[]> listings)
    {
        try
        {
            if (!listings.TryGetValue(folder, out string[]? entries))
            {
                entries = [.. Directory.EnumerateFileSystemEntries(folder)];
                listings.Add(folder, entries);
            }
            return OnDiskName(name, entries);
        }
        catch (UnauthorizedAccessException)
        {
            return name;
        }
        catch (DirectoryNotFoundException)
        {
            return name;
        }
        catch (IOException)
        {
            return name;
        }
    }

    /// <summary>The entry that names a segment on disk: an exact match first, else a case-insensitive one, else the segment.</summary>
    /// <param name="name">The segment as typed.</param>
    /// <param name="entries">The folder's entries, by name or by full path.</param>
    /// <returns>The matching entry's name, else the segment as typed.</returns>
    public static string OnDiskName(string name, IEnumerable<string> entries)
    {
        string? folded = null;
        foreach (string entry in entries)
        {
            string entryName = Path.GetFileName(entry);
            if (string.Equals(entryName, name, StringComparison.Ordinal))
            {
                return entryName;
            }
            if (folded is null && string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
            {
                folded = entryName;
            }
        }
        return folded ?? name;
    }
}
