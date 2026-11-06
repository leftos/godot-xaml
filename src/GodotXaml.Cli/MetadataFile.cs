using GodotXaml.Core.Metadata;
using GodotXaml.Core.Projects;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>The Godot versions a run carries, and how it loads one's metadata.</summary>
/// <param name="Carried">The versions the run carries (<c>4.7</c>).</param>
/// <param name="Load">Loads a carried version's metadata.</param>
internal sealed record MetadataVersions(IReadOnlyList<string> Carried, Func<string, GodotMetadata> Load)
{
    /// <summary>The versions this build bundles, and their loader.</summary>
    internal static MetadataVersions Bundled { get; } = new(GodotMetadata.BundledVersions, GodotMetadata.LoadBundled);
}

/// <summary>
/// The metadata a run reads and writes each file with: the <c>--metadata</c> file, which reads and writes every file, else
/// the version the file's <c>x:Godot</c> marker names when the run carries it, else the Godot version the file's project
/// names when the run carries that, else the newest version the run carries.
/// </summary>
internal sealed class MetadataChoice
{
    private readonly MetadataVersions versions;
    private readonly GodotMetadata? given;
    private readonly string workingDirectory;
    private readonly TextWriter output;
    private readonly Dictionary<string, GodotMetadata> loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> warned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> projectRoots = new(InputFiles.PathComparer);
    private readonly Dictionary<string, ProjectVersion> projectVersions = new(InputFiles.PathComparer);
    private readonly HashSet<string> warnedProjects = new(InputFiles.PathComparer);
    private GodotMetadata? newest;

    private MetadataChoice(MetadataVersions versions, GodotMetadata? given, string workingDirectory, TextWriter output)
    {
        this.versions = versions;
        this.given = given;
        this.workingDirectory = workingDirectory;
        this.output = output;
    }

    /// <summary>The choice a run makes: the given metadata file, else the versions the run carries.</summary>
    /// <param name="givenPath">The <c>--metadata</c> file, or null to choose each file's metadata by its marker.</param>
    /// <param name="versions">The versions the run carries, and their loader.</param>
    /// <param name="workingDirectory">The folder a relative path is resolved against before its project is found.</param>
    /// <param name="error">Where a metadata file that cannot be loaded is reported, naming the path and the reason.</param>
    /// <param name="output">Where a choice's warning and a write's refusal go.</param>
    /// <returns>The choice, or null when the given metadata file could not be loaded.</returns>
    internal static MetadataChoice? For(string? givenPath, MetadataVersions versions, string workingDirectory, TextWriter error, TextWriter output)
    {
        if (givenPath is null)
        {
            return new MetadataChoice(versions, null, workingDirectory, output);
        }
        GodotMetadata? given = ReadFile(givenPath, error);
        return given is null ? null : new MetadataChoice(versions, given, workingDirectory, output);
    }

    /// <summary>The newest version a set of carried versions names, by <see cref="System.Version"/>.</summary>
    /// <param name="carriedVersions">The versions a run carries.</param>
    /// <returns>The newest one, as <c>GodotMetadata.LoadBundledForProject</c> picks the newest bundled version.</returns>
    internal static string Newest(IReadOnlyList<string> carriedVersions) =>
        carriedVersions.Count == 0
            ? throw new InvalidOperationException("This tool carries no metadata.")
            : GodotMetadata.NewestVersion(carriedVersions);

    /// <summary>The metadata a file's text is read with.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="text">The file's text.</param>
    /// <returns>The metadata, and the number of warning lines the choice printed for this file.</returns>
    internal (GodotMetadata Metadata, int Warnings) Of(string path, string text)
    {
        if (given is not null)
        {
            return (given, 0);
        }
        string? marker = XscnMarker.Read(text);
        if (marker is null)
        {
            return UnmarkedFor(path);
        }
        if (versions.Carried.Contains(marker, StringComparer.Ordinal))
        {
            return (ForVersion(marker), 0);
        }
        (GodotMetadata used, int projectWarnings) = UnmarkedFor(path);
        if (!warned.Add(marker))
        {
            return (used, projectWarnings);
        }
        output.WriteLine($"{path}: warning {CliCodes.MarkerNotCarried}: {XscnMarker.NotCarried(marker, used.Version)}");
        return (used, projectWarnings + 1);
    }

    /// <summary>
    /// The metadata a file with no marker is read with: the <c>--metadata</c> file when the run has one, else the Godot
    /// version its project names when the run carries it, else the newest version the run carries. Nothing is read from the
    /// text, so a <c>.tscn</c> or <c>.tres</c> source is not parsed as XML to find a marker it cannot have.
    /// </summary>
    /// <param name="path">The path whose project decides the version, and which the output prints.</param>
    /// <returns>The metadata, and the number of warning lines the choice printed for this file.</returns>
    internal (GodotMetadata Metadata, int Warnings) Unmarked(string path) => given is not null ? (given, 0) : UnmarkedFor(path);

    /// <summary>Whether a file's text may be written: not when its marker names a version the run does not carry.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="text">The file's text.</param>
    /// <returns>Whether the file may be written; when it may not, the refusal is printed.</returns>
    internal bool CanWrite(string path, string text)
    {
        if (given is not null)
        {
            return true;
        }
        string? marker = XscnMarker.Read(text);
        if (marker is null || versions.Carried.Contains(marker, StringComparer.Ordinal))
        {
            return true;
        }
        string used = UnmarkedFor(path).Metadata.Version;
        output.WriteLine($"{path}: error {CliCodes.MarkerNotWritten}: {XscnMarker.NotWritten(marker, used)}");
        return false;
    }

    // The metadata an unmarked file is read with, and the number of warning lines this call printed for its project: one
    // warning per project per run, for a project naming a version the run does not carry or whose project.godot cannot be
    // read.
    private (GodotMetadata Metadata, int Warnings) UnmarkedFor(string path)
    {
        if (ProjectRootOf(path) is not { } root)
        {
            return (NewestMetadata(), 0);
        }
        ProjectVersion project = ProjectVersionOf(root);
        if (project.Named is not null && versions.Carried.Contains(project.Named, StringComparer.Ordinal))
        {
            return (ForVersion(project.Named), 0);
        }
        GodotMetadata newest = NewestMetadata();
        if (project is { Named: null, Unreadable: false } || !warnedProjects.Add(root))
        {
            return (newest, 0);
        }
        string projectPath = Path.Combine(root, "project.godot");
        output.WriteLine($"{projectPath}: warning {CliCodes.ProjectVersionNotCarried}: {ProjectWarning(project, newest.Version)}");
        return (newest, 1);
    }

    private static string ProjectWarning(ProjectVersion project, string newest) =>
        project.Unreadable
            ? $"cannot be read; reading its unmarked files with {newest}"
            : $"Godot {project.Named} has no bundled metadata; reading its unmarked files with {newest}";

    // The nearest project.godot at or above a file, or null when no folder above it holds one.
    private string? ProjectRootOf(string path)
    {
        string fullPath = Path.GetFullPath(path, workingDirectory);
        if (!projectRoots.TryGetValue(fullPath, out string? root))
        {
            root = GodotProject.Find(fullPath);
            projectRoots.Add(fullPath, root);
        }
        return root;
    }

    // Each project's version is read once per run, whatever the number of its files.
    private ProjectVersion ProjectVersionOf(string root)
    {
        if (!projectVersions.TryGetValue(root, out ProjectVersion? project))
        {
            project = ReadProjectVersion(root);
            projectVersions.Add(root, project);
        }
        return project;
    }

    private static ProjectVersion ReadProjectVersion(string root)
    {
        try
        {
            return new ProjectVersion(GodotProjectVersion.Read(File.ReadAllText(Path.Combine(root, "project.godot"))), false);
        }
        catch (IOException)
        {
            return new ProjectVersion(null, true);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProjectVersion(null, true);
        }
    }

    /// <summary>The Godot version a project names, and whether its <c>project.godot</c> could be read at all.</summary>
    /// <param name="Named">The version (<c>4.7</c>), or null when the project names none.</param>
    /// <param name="Unreadable">Whether the <c>project.godot</c> could not be read.</param>
    private sealed record ProjectVersion(string? Named, bool Unreadable);

    private GodotMetadata NewestMetadata() => newest ??= ForVersion(Newest(versions.Carried));

    // Each carried version's metadata is loaded once per run.
    private GodotMetadata ForVersion(string version)
    {
        if (!loaded.TryGetValue(version, out GodotMetadata? metadata))
        {
            metadata = versions.Load(version);
            loaded.Add(version, metadata);
        }
        return metadata;
    }

    /// <summary>Loads a metadata file, reporting why it could not be loaded.</summary>
    /// <param name="path">The metadata file's path.</param>
    /// <param name="error">Where a failure is reported, naming the path and the reason.</param>
    /// <returns>The metadata, or null when it could not be loaded.</returns>
    internal static GodotMetadata? ReadFile(string path, TextWriter error)
    {
        if (Directory.Exists(path))
        {
            error.WriteLine($"{path}: is a folder, not a metadata file.");
            return null;
        }
        try
        {
            return GodotMetadata.Load(path);
        }
        catch (FileNotFoundException)
        {
            error.WriteLine($"{path}: no such file or folder.");
        }
        catch (DirectoryNotFoundException)
        {
            error.WriteLine($"{path}: no such file or folder.");
        }
        catch (InvalidDataException exception)
        {
            // The loader's own message already starts with the path.
            error.WriteLine(exception.Message);
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be read.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"{path}: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            error.WriteLine($"{path}: {exception.Message}");
        }
        return null;
    }
}
