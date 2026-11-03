using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>A file a command works on: the path the output prints, and what its extension tells the command.</summary>
/// <typeparam name="T">What the command reads from the extension.</typeparam>
/// <param name="Path">The path the output prints: the given path, or the path the enumeration found under it.</param>
/// <param name="Kind">What the extension maps to.</param>
internal sealed record InputFile<T>(string Path, T Kind);

/// <summary>The extensions a command accepts, matched case-insensitively, and the refusal printed for any other file.</summary>
/// <typeparam name="T">What each extension maps to.</typeparam>
internal sealed class AcceptedFiles<T>
    where T : notnull
{
    private readonly FrozenDictionary<string, T> byExtension;

    /// <summary>Creates the set.</summary>
    /// <param name="extensions">Each accepted extension, with its leading dot, and what it maps to.</param>
    /// <param name="refusal">The text after <c>{path}: </c> for a file given by name with another extension.</param>
    internal AcceptedFiles(IEnumerable<KeyValuePair<string, T>> extensions, string refusal)
    {
        byExtension = extensions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Refusal = refusal;
    }

    /// <summary>The text after <c>{path}: </c> for a file given by name with another extension.</summary>
    internal string Refusal { get; }

    /// <summary>What a path's extension maps to.</summary>
    /// <param name="path">The path.</param>
    /// <param name="kind">What it maps to, when accepted.</param>
    /// <returns>Whether the extension is accepted.</returns>
    internal bool TryGetKind(string path, [MaybeNullWhen(false)] out T kind) => byExtension.TryGetValue(Path.GetExtension(path), out kind);
}

/// <summary>Turns a command's paths into the files it works on: files as given, folders searched recursively.</summary>
internal static class InputFiles
{
    /// <summary>The <c>.xscn</c> and <c>.xres</c> extensions and the kind of document each holds.</summary>
    internal static readonly KeyValuePair<string, XscnDocumentKind>[] XscnExtensions =
    [
        new(".xscn", XscnDocumentKind.Scene),
        new(".xres", XscnDocumentKind.Resource),
    ];

    /// <summary>Collects the files of every given path, in command-line order, each file once.</summary>
    /// <typeparam name="T">What each accepted extension maps to.</typeparam>
    /// <param name="givenPaths">The paths as written on the command line.</param>
    /// <param name="accepted">The extensions the command works on.</param>
    /// <param name="error">Where a path that cannot be used is reported.</param>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <returns>Every file to work on, or null when a path cannot be used.</returns>
    internal static List<InputFile<T>>? Collect<T>(
        string[] givenPaths,
        AcceptedFiles<T> accepted,
        TextWriter error,
        Func<string, IEnumerable<string>> enumerate
    )
        where T : notnull
    {
        List<InputFile<T>> files = [];
        foreach (string given in givenPaths)
        {
            if (!Add(given, accepted, files, error, enumerate))
            {
                return null;
            }
        }
        return Distinct(files);
    }

    /// <summary>Reads every file's text before any of them is worked on, so a failure part way through leaves no half-done run.</summary>
    /// <typeparam name="T">What each file's extension maps to.</typeparam>
    /// <param name="files">The files.</param>
    /// <param name="error">Where a file that cannot be read is reported.</param>
    /// <returns>Each file with its text, or null when one cannot be read or is not UTF-8 text.</returns>
    internal static List<(InputFile<T> File, string Text)>? ReadAll<T>(List<InputFile<T>> files, TextWriter error)
    {
        List<(InputFile<T> File, string Text)> read = [];
        foreach (InputFile<T> file in files)
        {
            string? text = SourceText.Read(file.Path, error);
            if (text is null)
            {
                return null;
            }
            read.Add((file, text));
        }
        return read;
    }

    /// <summary>Writes a file as UTF-8 without a byte order mark, reporting why it could not be written.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="text">The text.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>Whether the file was written.</returns>
    internal static bool TryWrite(string path, string text, TextWriter error) =>
        TryWrite(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text), error);

    /// <summary>Writes a file's bytes, reporting why it could not be written.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>Whether the file was written.</returns>
    internal static bool TryWrite(string path, byte[] bytes, TextWriter error)
    {
        // The bytes go to a temporary file beside the target first, so a failure never leaves the target half written.
        // A symlink is written through to its final target, so the link stays a link. A hard link is split by the move:
        // .NET has no portable way to detect one.
        string? temporary = null;
        bool moved = false;
        try
        {
            // ResolveLinkTarget throws for a path that does not exist yet, so it is asked only of a link.
            var info = new FileInfo(path);
            string target = info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
            temporary = $"{target}.godot-xaml-{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
            moved = true;
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be written.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
        }
        finally
        {
            if (!moved && temporary is not null && File.Exists(temporary))
            {
                TryDelete(temporary, error);
            }
        }
        return moved;
    }

    /// <summary>Writes a file in the staging folder, which already keeps a failed write off the target, with no temporary file of its own.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="text">The text, written as UTF-8 without a byte order mark.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>Whether the file was written.</returns>
    internal static bool TryWriteStaged(string path, string text, TextWriter error)
    {
        try
        {
            File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be written.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
        }
        return false;
    }

    /// <summary>Deletes a file, reporting why it could not be deleted.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>Whether the file is gone.</returns>
    internal static bool TryDelete(string path, TextWriter error)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be deleted.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
        }
        return false;
    }

    /// <summary>How two full paths are compared: ignoring case on Windows' case-insensitive file system, else exactly.</summary>
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // The first spelling of a path wins.
    private static List<InputFile<T>> Distinct<T>(List<InputFile<T>> files)
    {
        var seen = new HashSet<string>(PathComparer);
        List<InputFile<T>> distinct = [];
        foreach (InputFile<T> file in files)
        {
            if (seen.Add(Path.GetFullPath(file.Path)))
            {
                distinct.Add(file);
            }
        }
        return distinct;
    }

    private static bool Add<T>(
        string given,
        AcceptedFiles<T> accepted,
        List<InputFile<T>> files,
        TextWriter error,
        Func<string, IEnumerable<string>> enumerate
    )
        where T : notnull
    {
        if (Directory.Exists(given))
        {
            return TryAddFolder(given, accepted, files, error, enumerate);
        }
        if (!File.Exists(given))
        {
            error.WriteLine($"{given}: no such file or folder.");
            return false;
        }
        if (!accepted.TryGetKind(given, out T? kind))
        {
            error.WriteLine($"{given}: {accepted.Refusal}");
            return false;
        }
        files.Add(new InputFile<T>(given, kind));
        return true;
    }

    private static bool TryAddFolder<T>(
        string folder,
        AcceptedFiles<T> accepted,
        List<InputFile<T>> files,
        TextWriter error,
        Func<string, IEnumerable<string>> enumerate
    )
        where T : notnull
    {
        List<string> found = [];
        if (!Walk(folder, found, error, enumerate))
        {
            return false;
        }
        files.AddRange(Ordered(folder, found, accepted));
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
    private static IEnumerable<InputFile<T>> Ordered<T>(string folder, List<string> found, AcceptedFiles<T> accepted)
        where T : notnull
    {
        List<InputFile<T>> candidates = [];
        foreach (string path in found)
        {
            if (accepted.TryGetKind(path, out T? kind))
            {
                candidates.Add(new InputFile<T>(path, kind));
            }
        }
        return candidates.OrderBy(candidate => RelativeKey(folder, candidate.Path), StringComparer.Ordinal);
    }

    private static string RelativeKey(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');
}
