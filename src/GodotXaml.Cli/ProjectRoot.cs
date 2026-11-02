namespace GodotXaml.Cli;

/// <summary>The Godot project a file belongs to: the nearest folder at or above it holding <c>project.godot</c>.</summary>
internal static class ProjectRoot
{
    private const string FileName = "project.godot";

    /// <summary>Finds the project root of a file.</summary>
    /// <param name="fileFullPath">The file's full path.</param>
    /// <returns>The nearest root folder's full path, or null when no folder at or above the file holds <c>project.godot</c>.</returns>
    internal static string? Find(string fileFullPath)
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
    internal static string ResPath(string root, string fileFullPath)
    {
        string[] segments = Path.GetRelativePath(root, Path.GetFullPath(fileFullPath))
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        string folder = root;
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = OnDiskName(folder, segments[index]);
            folder = Path.Combine(folder, segments[index]);
        }
        return "res://" + string.Join('/', segments);
    }

    // The name a folder's entry has on disk, so a path typed in another case still names the file Godot loads. A
    // folder that cannot be listed, and a name with no entry yet such as an output, keeps the name it was given.
    private static string OnDiskName(string folder, string name)
    {
        try
        {
            return OnDiskName(name, Directory.EnumerateFileSystemEntries(folder));
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
    internal static string OnDiskName(string name, IEnumerable<string> entries)
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
