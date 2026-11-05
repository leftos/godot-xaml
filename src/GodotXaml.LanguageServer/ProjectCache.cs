using GodotXaml.Core.Metadata;
using GodotXaml.Core.Projects;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>The project a document belongs to and the metadata it is read with.</summary>
/// <param name="Root">The project root's full path, or null outside any project.</param>
/// <param name="Metadata">The metadata.</param>
/// <param name="Version">The Godot minor version of the metadata.</param>
/// <param name="Marker">The <c>x:Godot</c> marker the document names, or null when it has none.</param>
/// <param name="MarkerCarried">Whether the document's marker names a version the server carries; true when it has none.</param>
internal sealed record ProjectContext(string? Root, GodotMetadata Metadata, string Version, string? Marker, bool MarkerCarried);

/// <summary>
/// Finds each document's project, the nearest folder above it holding <c>project.godot</c>, cached per folder, and loads
/// the metadata its <c>config/features</c> names once per version. A document's <c>x:Godot</c> marker then names the
/// version it is read with, when the server carries that version; a marker it does not carry reads with the newest it
/// carries. Each project's notices (the version in use, a version with no bundled metadata, a load failure) are sent once,
/// the first time the project is seen, and a marker the server does not carry is sent once per marker value.
/// </summary>
internal sealed class ProjectCache(MetadataLoader loader, IClientMessages client)
{
    private const string Outside = "";

    private const string ProjectFile = "project.godot";

    private readonly Lock gate = new();
    private readonly Dictionary<string, string?> rootsByFolder = new(DocumentStore.PathComparer);
    private readonly Dictionary<string, ProjectContext?> projects = new(DocumentStore.PathComparer);
    private readonly Dictionary<string, LoadedMetadata> byVersion = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarkerRead> markers = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnedMarkers = new(StringComparer.Ordinal);

    /// <summary>The project of a document, sending its notices the first time it is seen.</summary>
    /// <param name="document">The document, whose marker names the metadata it is read with.</param>
    /// <returns>The project, or null when its metadata could not be loaded.</returns>
    public async Task<ProjectContext?> ResolveAsync(OpenDocument document)
    {
        List<Notice> notices = [];
        ProjectContext? context;
        lock (gate)
        {
            context = Resolve(document, notices);
        }

        foreach (Notice notice in notices)
        {
            await (notice.Shown ? client.ShowAsync(notice.Type, notice.Message) : client.LogAsync(notice.Type, notice.Message)).ConfigureAwait(false);
        }
        return context;
    }

    // Called under the gate.
    private ProjectContext? Resolve(OpenDocument document, List<Notice> notices)
    {
        string? root = null;
        if (document.Path is not null)
        {
            string folder = Path.GetDirectoryName(document.Path) ?? document.Path;
            if (!rootsByFolder.TryGetValue(folder, out root))
            {
                root = GodotProject.Find(document.Path);
                rootsByFolder.Add(folder, root);
            }
        }

        string key = root ?? Outside;
        if (!projects.TryGetValue(key, out ProjectContext? project))
        {
            project = Load(root, notices);
            projects.Add(key, project);
        }
        if (project is null)
        {
            return null;
        }

        return MarkerOf(document) is { } marker ? Marked(project, marker, document, notices) : project;
    }

    // A project read with a marker's metadata: the marker's version when the server carries it, else the newest it carries,
    // with one message per marker value it does not carry.
    private ProjectContext? Marked(ProjectContext project, string marker, OpenDocument document, List<Notice> notices)
    {
        if (!byVersion.TryGetValue(marker, out LoadedMetadata? loaded))
        {
            try
            {
                loaded = loader(marker);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                notices.Add(
                    new Notice(
                        true,
                        MessageType.Error,
                        $"godot-xaml could not load the Godot {marker} metadata for {NameOf(document)}: {exception.Message}"
                    )
                );
                return null;
            }
            byVersion.Add(marker, loaded);
        }

        bool carried = string.Equals(marker, loaded.Version, StringComparison.Ordinal);
        if (!carried && warnedMarkers.Add(marker))
        {
            notices.Add(new Notice(true, MessageType.Warning, $"{NameOf(document)}: {XscnMarker.NotCarried(marker, loaded.Version)}"));
        }

        return new ProjectContext(project.Root, loaded.Metadata, loaded.Version, marker, carried);
    }

    // Called under the gate: the document's marker, read once per update of its text.
    private string? MarkerOf(OpenDocument document)
    {
        if (markers.TryGetValue(document.Uri, out MarkerRead read) && read.Stamp == document.Stamp)
        {
            return read.Marker;
        }

        string? marker = XscnMarker.Read(document.Text);
        markers[document.Uri] = new MarkerRead(document.Stamp, marker);
        return marker;
    }

    private ProjectContext? Load(string? root, List<Notice> notices)
    {
        string? unreadable = null;
        string? projectVersion = root is null ? null : ReadVersion(root, out unreadable);
        try
        {
            if (!byVersion.TryGetValue(projectVersion ?? Outside, out LoadedMetadata? loaded))
            {
                loaded = loader(projectVersion);
                byVersion.Add(projectVersion ?? Outside, loaded);
            }

            AddLoaded(root, loaded, unreadable, notices);
            return new ProjectContext(root, loaded.Metadata, loaded.Version, null, true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string where = root ?? "documents outside any project";
            notices.Add(new Notice(true, MessageType.Error, $"godot-xaml could not load the Godot metadata for {where}: {exception.Message}"));
            return null;
        }
    }

    // The notices of a project seen for the first time: the version in use, and why it is not the project's.
    private static void AddLoaded(string? root, LoadedMetadata loaded, string? unreadable, List<Notice> notices)
    {
        if (root is null)
        {
            notices.Add(
                new Notice(
                    false,
                    MessageType.Info,
                    $"A document outside any Godot project is read with the Godot {loaded.Version} metadata; res:// checks are off."
                )
            );
            return;
        }

        if (unreadable is not null)
        {
            string file = Path.Combine(root, ProjectFile);
            notices.Add(
                new Notice(true, MessageType.Warning, $"{file} could not be read ({unreadable}); using the Godot {loaded.Version} metadata.")
            );
        }
        else if (loaded.Warning is not null)
        {
            notices.Add(new Notice(true, MessageType.Warning, $"{root}: {loaded.Warning}."));
        }
        notices.Add(new Notice(false, MessageType.Info, $"{root}: using the Godot {loaded.Version} metadata."));
    }

    // The version project.godot names, or null with why the file could not be read.
    private static string? ReadVersion(string root, out string? unreadable)
    {
        unreadable = null;
        try
        {
            return GodotProjectVersion.Read(File.ReadAllText(Path.Combine(root, ProjectFile)));
        }
        catch (IOException exception)
        {
            unreadable = exception.Message;
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            unreadable = exception.Message;
            return null;
        }
    }

    // The document's file name, or the last, unescaped segment of its URI for a document not on disk.
    private static string NameOf(OpenDocument document) =>
        document.Path is null ? Uri.UnescapeDataString(document.Uri[(document.Uri.LastIndexOf('/') + 1)..]) : Path.GetFileName(document.Path);

    private readonly record struct MarkerRead(long Stamp, string? Marker);

    private sealed record Notice(bool Shown, MessageType Type, string Message);
}
