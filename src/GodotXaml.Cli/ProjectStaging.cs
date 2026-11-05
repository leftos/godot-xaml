namespace GodotXaml.Cli;

/// <summary>
/// The all-or-nothing part of a project run that writes many files: each output is written first under
/// <c>&lt;root&gt;/.godot-xaml-staging/&lt;stamp&gt;-&lt;8 hex digits&gt;/</c> at its path under the root, the originals are
/// backed up to <c>.godot-xaml-backup/&lt;stamp&gt;/</c> through <see cref="OriginalsBackup"/>, then each staged output is moved
/// into place. Disposing removes the staging folder, whatever the run's outcome. Shared by <c>convert --project</c> and
/// <c>upgrade</c>.
/// </summary>
/// <param name="root">The project root's full path, with no trailing separator.</param>
/// <param name="noBackup">Whether the run keeps no backup, so <see cref="TryBackUp"/> copies nothing.</param>
/// <param name="output">Where the backup's line goes.</param>
/// <param name="error">Where a failure goes.</param>
internal sealed class ProjectStaging(string root, bool noBackup, TextWriter output, TextWriter error) : IDisposable
{
    /// <summary>The folder under a project root that holds one staging folder per run.</summary>
    internal const string FolderName = ".godot-xaml-staging";

    /// <summary>This run's staging folder, once <see cref="TryCreate"/> made it.</summary>
    internal string? Folder { get; private set; }

    /// <summary>The stamped folder the originals were backed up to, once <see cref="TryBackUp"/> made it.</summary>
    internal string? Backup { get; private set; }

    /// <summary>Whether the run keeps no backup.</summary>
    internal bool NoBackup => noBackup;

    /// <summary>Makes this run's staging folder; a failure prints <c>{parent}: cannot be written.</c>.</summary>
    /// <returns>Whether the folder was made.</returns>
    internal bool TryCreate()
    {
        string parent = Path.Combine(root, FolderName);
        if (!OriginalsBackup.TryCreateUnique(parent, out string? staging))
        {
            error.WriteLine($"{parent}: cannot be written.");
            return false;
        }
        Folder = staging;
        return true;
    }

    /// <summary>Where an output is staged: its path under the root, under the staging folder.</summary>
    /// <param name="target">The output's full path, under the root.</param>
    /// <returns>The staged path.</returns>
    internal string PathOf(string target) => Path.Combine(Folder!, Path.GetRelativePath(root, target));

    /// <summary>Makes the staging folder and writes every output's text in it; stops at the first that fails, reported.</summary>
    /// <param name="files">Each output's full path under the root and its text.</param>
    /// <returns>Each output's staged path, in order, or null when one could not be staged.</returns>
    internal List<string>? TryStage(IReadOnlyList<(string Target, string Text)> files)
    {
        if (Folder is null && !TryCreate())
        {
            return null;
        }
        List<string> staged = [];
        foreach ((string target, string text) in files)
        {
            string path = PathOf(target);
            if (!TryCreateFolder(Path.GetDirectoryName(path)!) || !InputFiles.TryWriteStaged(path, text, error))
            {
                return null;
            }
            staged.Add(path);
        }
        return staged;
    }

    /// <summary>
    /// Backs up each file once, a path named twice copied once, and prints <c>{n} file(s) backed up to {folder}</c>; does
    /// nothing under the no-backup flag.
    /// </summary>
    /// <param name="files">The full paths of the files to copy from disk, each under the root.</param>
    /// <param name="read">Files under the root already read, each backed up from the bytes held.</param>
    /// <returns>Whether every file was backed up, or the run keeps no backup.</returns>
    internal bool TryBackUp(IReadOnlyList<string> files, IReadOnlyList<(string Path, byte[] Bytes)> read)
    {
        if (noBackup)
        {
            return true;
        }
        var seen = new HashSet<string>(InputFiles.PathComparer);
        List<(string Path, byte[] Bytes)> held = [.. read.Where(file => seen.Add(Path.GetFullPath(file.Path)))];
        List<string> copied = [.. files.Where(file => seen.Add(Path.GetFullPath(file)))];
        if (!OriginalsBackup.TryCopy(root, copied, held, error, out string? folder))
        {
            return false;
        }
        Backup = folder;
        output.WriteLine($"{copied.Count + held.Count} file(s) backed up to {folder}");
        return true;
    }

    /// <summary>Moves a staged file over its target.</summary>
    /// <param name="from">The staged file.</param>
    /// <param name="to">The target, replaced when it exists.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>Whether the file was moved.</returns>
    internal static bool TryMove(string from, string to, TextWriter error)
    {
        try
        {
            File.Move(from, to, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{to}: cannot be written.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{to}: access denied.");
        }
        return false;
    }

    /// <summary>
    /// Removes the staging folder, and the folder above it, with its .gitignore, once no other run's is left in it; a
    /// failure is reported and the run goes on.
    /// </summary>
    public void Dispose()
    {
        if (Folder is null)
        {
            return;
        }
        try
        {
            Directory.Delete(Folder, recursive: true);
            string parent = Path.GetDirectoryName(Folder)!;
            string gitIgnore = Path.Combine(parent, OriginalsBackup.GitIgnoreName);
            if (Directory.EnumerateFileSystemEntries(parent).All(entry => entry == gitIgnore))
            {
                File.Delete(gitIgnore);
                Directory.Delete(parent);
            }
        }
        catch (IOException)
        {
            error.WriteLine($"{Folder}: cannot be deleted.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{Folder}: access denied.");
        }
    }

    private bool TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{folder}: cannot be written.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{folder}: access denied.");
        }
        return false;
    }
}
