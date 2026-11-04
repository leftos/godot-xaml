using System.Text.RegularExpressions;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Projects;
using GodotXaml.Core.Xscn;

namespace GodotXaml.LanguageServer;

/// <summary>One open buffer as the client last sent it.</summary>
/// <param name="Uri">The URI the client names it by.</param>
/// <param name="Path">The file's full path for a <c>file:</c> URI, else null.</param>
/// <param name="Kind">Scene or resource, by the name's ending.</param>
/// <param name="Version">The client's version of the text.</param>
/// <param name="Text">The text.</param>
/// <param name="Stamp">Which update of the store made this entry; a reopened document gets a new one.</param>
internal sealed record OpenDocument(string Uri, string? Path, XscnDocumentKind Kind, int Version, string Text, long Stamp);

/// <summary>The documents the client has open, by URI and by path.</summary>
/// <param name="stderr">Where a failed read of a referenced file is logged; nowhere when null.</param>
internal sealed partial class DocumentStore(TextWriter? stderr = null) : ISceneFiles
{
    private readonly TextWriter log = stderr ?? TextWriter.Null;
    private readonly Lock gate = new();
    private readonly Dictionary<string, OpenDocument> byUri = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpenDocument> byPath = new(PathComparer);
    private long stamps;

    /// <summary>How paths compare: ignoring case on Windows, as its file system does.</summary>
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Whether the server reads a URI, and as what: <c>.xres</c> a resource, <c>.xscn</c> a scene.</summary>
    /// <param name="uri">The URI.</param>
    /// <param name="path">The full path of a <c>file:</c> URI, else null.</param>
    /// <param name="kind">Scene or resource.</param>
    /// <returns>Whether the URI names an <c>.xscn</c> or an <c>.xres</c>.</returns>
    public static bool TryDescribe(string uri, out string? path, out XscnDocumentKind kind)
    {
        path = System.Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.IsFile ? Path.GetFullPath(LocalPathOf(parsed)) : null;
        string name = path ?? uri;
        kind = XscnDocumentKind.Scene;
        if (name.EndsWith(".xres", StringComparison.OrdinalIgnoreCase))
        {
            kind = XscnDocumentKind.Resource;
            return true;
        }
        return name.EndsWith(".xscn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens a buffer, replacing one already open under the URI.</summary>
    /// <param name="uri">The URI.</param>
    /// <param name="version">The client's version.</param>
    /// <param name="text">The text.</param>
    /// <returns>The buffer, or null when the URI is not a document the server reads.</returns>
    public OpenDocument? Open(string uri, int version, string text)
    {
        if (!TryDescribe(uri, out string? path, out XscnDocumentKind kind))
        {
            return null;
        }

        lock (gate)
        {
            return Put(new OpenDocument(uri, path, kind, version, text, ++stamps));
        }
    }

    /// <summary>Replaces an open buffer's text.</summary>
    /// <param name="uri">The URI.</param>
    /// <param name="version">The client's new version.</param>
    /// <param name="text">The whole new text.</param>
    /// <returns>The buffer, or null when no buffer is open under the URI.</returns>
    public OpenDocument? Change(string uri, int version, string text)
    {
        lock (gate)
        {
            return byUri.TryGetValue(uri, out OpenDocument? open) ? Put(open with { Version = version, Text = text, Stamp = ++stamps }) : null;
        }
    }

    /// <summary>Drops a buffer.</summary>
    /// <param name="uri">The URI.</param>
    /// <returns>Whether a buffer was open under it.</returns>
    public bool Close(string uri)
    {
        lock (gate)
        {
            if (!byUri.Remove(uri, out OpenDocument? open))
            {
                return false;
            }
            if (open.Path is not null && byPath.TryGetValue(open.Path, out OpenDocument? same) && same.Stamp == open.Stamp)
            {
                byPath.Remove(open.Path);
            }
            return true;
        }
    }

    /// <summary>Whether a buffer is still the newest one under its URI: not changed, closed or reopened since.</summary>
    /// <param name="document">The buffer.</param>
    /// <returns>Whether it is current.</returns>
    public bool IsCurrent(OpenDocument document)
    {
        lock (gate)
        {
            return byUri.TryGetValue(document.Uri, out OpenDocument? open) && open.Stamp == document.Stamp;
        }
    }

    /// <summary>
    /// A scene a reference names: its open buffer when the client has it open, else the file on disk. A <c>res://</c>
    /// reference resolves under the referrer's project, any other against the referrer's folder; the uid is not read.
    /// </summary>
    /// <param name="referrer">The full path of the file holding the reference.</param>
    /// <param name="reference">The scene's path as the file writes it.</param>
    /// <param name="uid">The reference's uid, unread.</param>
    /// <returns>The scene keyed by its full path, or null when it is neither open nor readable on disk.</returns>
    public SceneFile? Read(string referrer, string reference, string? uid)
    {
        string? full = Resolve(referrer, reference);
        if (full is null)
        {
            return null;
        }

        lock (gate)
        {
            if (byPath.TryGetValue(full, out OpenDocument? open))
            {
                return new SceneFile(open.Path!, open.Text);
            }
        }

        try
        {
            return File.Exists(full) ? new SceneFile(full, File.ReadAllText(full)) : null;
        }
        catch (IOException exception)
        {
            log.WriteLine($"Error: {full} could not be read: {exception.Message}");
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            log.WriteLine($"Error: {full} could not be read: {exception.Message}");
            return null;
        }
    }

    // VS Code writes a Windows path's drive colon encoded (file:///c%3A/x), which Uri leaves as the path /c:/x; on
    // Windows that leading slash would make GetFullPath put the current drive in front.
    private static string LocalPathOf(Uri uri)
    {
        string local = uri.LocalPath;
        return OperatingSystem.IsWindows() && SlashBeforeDrive().IsMatch(local) ? local[1..] : local;
    }

    [GeneratedRegex("^/[A-Za-z]:")]
    private static partial Regex SlashBeforeDrive();

    private static string? Resolve(string referrer, string reference)
    {
        if (reference.StartsWith("res://", StringComparison.Ordinal))
        {
            string? root = GodotProject.Find(referrer);
            return root is null ? null : Path.GetFullPath(GodotProject.DiskPath(root, reference));
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(referrer));
        return folder is null ? null : Path.GetFullPath(Path.Combine(folder, reference));
    }

    // Called under the gate.
    private OpenDocument Put(OpenDocument document)
    {
        byUri[document.Uri] = document;
        if (document.Path is not null)
        {
            byPath[document.Path] = document;
        }
        return document;
    }
}
