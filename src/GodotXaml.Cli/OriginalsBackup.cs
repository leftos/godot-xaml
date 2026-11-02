using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GodotXaml.Cli;

/// <summary>
/// Copies the files a <c>convert --project</c> run will replace, edit or delete into
/// <c>&lt;root&gt;/.godot-xaml-backup/&lt;UTC yyyyMMdd-HHmmss&gt;/</c>, each at its path under the root. The folder's name
/// starts with a dot, so Godot and every walk of this tool leave it out.
/// </summary>
internal static class OriginalsBackup
{
    /// <summary>The folder under a project root that holds one stamped folder per run.</summary>
    internal const string FolderName = ".godot-xaml-backup";

    /// <summary>Backs up each file, stopping at the first that cannot be; the copies made so far stay.</summary>
    /// <param name="root">The project root's full path.</param>
    /// <param name="files">The full paths of the files to copy from disk, each under the root.</param>
    /// <param name="read">Files under the root already read, each backed up from the bytes held rather than read again.</param>
    /// <param name="error">Where <c>{path}: cannot be backed up.</c> is printed for the first failure.</param>
    /// <param name="folder">The stamped folder the copies went to, when every file was backed up.</param>
    /// <returns>Whether every file was backed up.</returns>
    internal static bool TryCopy(
        string root,
        IReadOnlyList<string> files,
        IReadOnlyList<(string Path, byte[] Bytes)> read,
        TextWriter error,
        [NotNullWhen(true)] out string? folder
    )
    {
        folder = null;
        if (!TryCreateStamped(Path.Combine(root, FolderName), out string? created))
        {
            error.WriteLine($"{Path.Combine(root, FolderName)}: cannot be backed up.");
            return false;
        }
        foreach (string file in files)
        {
            if (!TryCopyOne(file, null, Path.Combine(created, Path.GetRelativePath(root, file))))
            {
                error.WriteLine($"{file}: cannot be backed up.");
                return false;
            }
        }
        foreach ((string file, byte[] bytes) in read)
        {
            if (!TryCopyOne(file, bytes, Path.Combine(created, Path.GetRelativePath(root, file))))
            {
                error.WriteLine($"{file}: cannot be backed up.");
                return false;
            }
        }
        folder = created;
        return true;
    }

    /// <summary>
    /// Creates <c>{parent}/{UTC yyyyMMdd-HHmmss}</c>, or with <c>-2</c>, <c>-3</c> and so on appended when that name is
    /// taken, so a second run in one second never shares the first one's folder.
    /// </summary>
    /// <param name="parent">The folder to create it in; it is created too when missing.</param>
    /// <param name="folder">The folder created.</param>
    /// <returns>Whether the folder was created.</returns>
    internal static bool TryCreateStamped(string parent, [NotNullWhen(true)] out string? folder)
    {
        string stamp = Stamp();
        string candidate = Path.Combine(parent, stamp);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(parent, $"{stamp}-{suffix}");
        }
        return TryCreate(candidate, out folder);
    }

    /// <summary>
    /// Creates <c>{parent}/{UTC yyyyMMdd-HHmmss}-{8 hex digits}</c>, the digits from a new Guid, so two runs at once never
    /// share one.
    /// </summary>
    /// <param name="parent">The folder to create it in; it is created too when missing.</param>
    /// <param name="folder">The folder created.</param>
    /// <returns>Whether the folder was created.</returns>
    internal static bool TryCreateUnique(string parent, [NotNullWhen(true)] out string? folder) =>
        TryCreate(Path.Combine(parent, $"{Stamp()}-{Guid.NewGuid().ToString("N")[..8]}"), out folder);

    /// <summary>The file in a parent folder that tells git to leave the folder out, as a Python venv does.</summary>
    internal const string GitIgnoreName = ".gitignore";

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    // A failure is reported by the caller, which names what the folder was for. The parent gets a .gitignore holding
    // "*" unless it has one already, so git leaves the parent out without an edit to the project's own .gitignore.
    private static bool TryCreate(string candidate, [NotNullWhen(true)] out string? folder)
    {
        folder = null;
        try
        {
            Directory.CreateDirectory(candidate);
            string gitIgnore = Path.Combine(Path.GetDirectoryName(candidate)!, GitIgnoreName);
            if (!File.Exists(gitIgnore))
            {
                File.WriteAllText(gitIgnore, "*\n");
            }
            folder = candidate;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A copy from disk when bytes is null, else the bytes held; never over an existing file.
    private static bool TryCopyOne(string file, byte[]? bytes, string copy)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            if (bytes is null)
            {
                File.Copy(file, copy, overwrite: false);
            }
            else
            {
                using var stream = new FileStream(copy, FileMode.CreateNew, FileAccess.Write);
                stream.Write(bytes);
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
