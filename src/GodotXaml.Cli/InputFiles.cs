using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>A file to check: the path the output prints, and what kind of document it holds.</summary>
/// <param name="Path">The path the output prints: the given path, or the path the enumeration found under it.</param>
/// <param name="Kind">Scene or resource, from the extension.</param>
internal sealed record CheckFile(string Path, XscnDocumentKind Kind);

/// <summary>Turns the command's paths into the files to check: files as given, folders searched recursively.</summary>
internal static class InputFiles
{
    /// <summary>Collects the files of every given path, in command-line order, each file once.</summary>
    /// <param name="givenPaths">The paths as written on the command line.</param>
    /// <param name="error">Where a path that cannot be used is reported.</param>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <returns>Every file to check, or null when a path cannot be used.</returns>
    internal static List<CheckFile>? Collect(string[] givenPaths, TextWriter error, Func<string, IEnumerable<string>> enumerate)
    {
        List<CheckFile> files = [];
        foreach (string given in givenPaths)
        {
            if (!Add(given, files, error, enumerate))
            {
                return null;
            }
        }
        return Distinct(files);
    }

    // The first spelling of a path wins, and Windows' case-insensitive file system is matched.
    private static List<CheckFile> Distinct(List<CheckFile> files)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        List<CheckFile> distinct = [];
        foreach (CheckFile file in files)
        {
            if (seen.Add(Path.GetFullPath(file.Path)))
            {
                distinct.Add(file);
            }
        }
        return distinct;
    }

    private static bool Add(string given, List<CheckFile> files, TextWriter error, Func<string, IEnumerable<string>> enumerate)
    {
        if (Directory.Exists(given))
        {
            return TryAddFolder(given, files, error, enumerate);
        }
        if (!File.Exists(given))
        {
            error.WriteLine($"{given}: no such file or folder.");
            return false;
        }
        XscnDocumentKind? kind = KindOf(given);
        if (kind is null)
        {
            error.WriteLine($"{given}: only .xscn and .xres files can be checked.");
            return false;
        }
        files.Add(new CheckFile(given, kind.Value));
        return true;
    }

    private static bool TryAddFolder(string folder, List<CheckFile> files, TextWriter error, Func<string, IEnumerable<string>> enumerate)
    {
        List<string> found = [];
        if (!Walk(folder, found, error, enumerate))
        {
            return false;
        }
        files.AddRange(Ordered(folder, found));
        return true;
    }

    // One folder at a time, so a failure names the folder it concerns rather than the one the walk started from.
    private static bool Walk(string folder, List<string> found, TextWriter error, Func<string, IEnumerable<string>> enumerate)
    {
        try
        {
            found.AddRange(enumerate(folder));
            foreach (string subfolder in Directory.EnumerateDirectories(folder))
            {
                if (!Walk(subfolder, found, error, enumerate))
                {
                    return false;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{folder}: access denied.");
            return false;
        }
        catch (IOException)
        {
            error.WriteLine($"{folder}: cannot be read.");
            return false;
        }
        return true;
    }

    // The order is the same on every OS: the path under the folder, with '/' as the separator, compared ordinal.
    private static IEnumerable<CheckFile> Ordered(string folder, List<string> found)
    {
        List<(string Path, XscnDocumentKind Kind)> candidates = [];
        foreach (string path in found)
        {
            XscnDocumentKind? kind = KindOf(path);
            if (kind is not null)
            {
                candidates.Add((path, kind.Value));
            }
        }
        return candidates
            .OrderBy(candidate => RelativeKey(folder, candidate.Path), StringComparer.Ordinal)
            .Select(candidate => new CheckFile(candidate.Path, candidate.Kind));
    }

    private static string RelativeKey(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

    private static XscnDocumentKind? KindOf(string path)
    {
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".xscn", StringComparison.OrdinalIgnoreCase))
        {
            return XscnDocumentKind.Scene;
        }
        if (string.Equals(extension, ".xres", StringComparison.OrdinalIgnoreCase))
        {
            return XscnDocumentKind.Resource;
        }
        return null;
    }
}
