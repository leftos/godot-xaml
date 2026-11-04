using GodotXaml.Core.Metadata;
using GodotXaml.Core.Projects;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>The project a document belongs to and the metadata it is read with.</summary>
/// <param name="Root">The project root's full path, or null outside any project.</param>
/// <param name="Metadata">The metadata.</param>
/// <param name="Version">The Godot minor version of the metadata.</param>
internal sealed record ProjectContext(string? Root, GodotMetadata Metadata, string Version);

/// <summary>
/// Finds each document's project, the nearest folder above it holding <c>project.godot</c>, cached per folder, and loads
/// the metadata its <c>config/features</c> names once per version. Each project's notices (the version in use, a
/// version with no bundled metadata, a load failure) are sent once, the first time the project is seen.
/// </summary>
internal sealed class ProjectCache(MetadataLoader loader, IClientMessages client)
{
    private const string Outside = "";

    private const string ProjectFile = "project.godot";

    private readonly Lock gate = new();
    private readonly Dictionary<string, string?> rootsByFolder = new(DocumentStore.PathComparer);
    private readonly Dictionary<string, ProjectContext?> projects = new(DocumentStore.PathComparer);
    private readonly Dictionary<string, LoadedMetadata> byVersion = new(StringComparer.Ordinal);

    /// <summary>The project of a document, sending its notices the first time it is seen.</summary>
    /// <param name="path">The document's full path, or null for a document not on disk.</param>
    /// <returns>The project, or null when its metadata could not be loaded.</returns>
    public async Task<ProjectContext?> ResolveAsync(string? path)
    {
        List<Notice> notices = [];
        ProjectContext? context;
        lock (gate)
        {
            context = Resolve(path, notices);
        }

        foreach (Notice notice in notices)
        {
            await (notice.Shown ? client.ShowAsync(notice.Type, notice.Message) : client.LogAsync(notice.Type, notice.Message)).ConfigureAwait(false);
        }
        return context;
    }

    // Called under the gate.
    private ProjectContext? Resolve(string? path, List<Notice> notices)
    {
        string? root = null;
        if (path is not null)
        {
            string folder = Path.GetDirectoryName(path) ?? path;
            if (!rootsByFolder.TryGetValue(folder, out root))
            {
                root = GodotProject.Find(path);
                rootsByFolder.Add(folder, root);
            }
        }

        string key = root ?? Outside;
        if (projects.TryGetValue(key, out ProjectContext? known))
        {
            return known;
        }

        ProjectContext? context = Load(root, notices);
        projects.Add(key, context);
        return context;
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
            return new ProjectContext(root, loaded.Metadata, loaded.Version);
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

    private sealed record Notice(bool Shown, MessageType Type, string Message);
}
