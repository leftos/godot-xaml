using GodotXaml.Core.Metadata;
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
/// the version the file's <c>x:Godot</c> marker names when the run carries it, else the newest version the run carries.
/// </summary>
internal sealed class MetadataChoice
{
    private readonly MetadataVersions versions;
    private readonly GodotMetadata? given;
    private readonly TextWriter output;
    private readonly Dictionary<string, GodotMetadata> loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> warned = new(StringComparer.Ordinal);
    private GodotMetadata? newest;

    private MetadataChoice(MetadataVersions versions, GodotMetadata? given, TextWriter output)
    {
        this.versions = versions;
        this.given = given;
        this.output = output;
    }

    /// <summary>The choice a run makes: the given metadata file, else the versions the run carries.</summary>
    /// <param name="givenPath">The <c>--metadata</c> file, or null to choose each file's metadata by its marker.</param>
    /// <param name="versions">The versions the run carries, and their loader.</param>
    /// <param name="error">Where a metadata file that cannot be loaded is reported, naming the path and the reason.</param>
    /// <param name="output">Where a choice's warning and a write's refusal go.</param>
    /// <returns>The choice, or null when the given metadata file could not be loaded.</returns>
    internal static MetadataChoice? For(string? givenPath, MetadataVersions versions, TextWriter error, TextWriter output)
    {
        if (givenPath is null)
        {
            return new MetadataChoice(versions, null, output);
        }
        GodotMetadata? given = ReadFile(givenPath, error);
        return given is null ? null : new MetadataChoice(versions, given, output);
    }

    /// <summary>The newest version a set of carried versions names, by <see cref="System.Version"/>.</summary>
    /// <param name="carriedVersions">The versions a run carries.</param>
    /// <returns>The newest one, as <c>GodotMetadata.LoadBundledForProject</c> picks the newest bundled version.</returns>
    internal static string Newest(IReadOnlyList<string> carriedVersions) =>
        carriedVersions.Where(version => System.Version.TryParse(version, out _)).MaxBy(version => System.Version.Parse(version))
        ?? throw new InvalidOperationException("This tool carries no metadata.");

    /// <summary>The metadata a file's text is read with.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="text">The file's text.</param>
    /// <returns>The metadata, and whether the choice printed a warning for the file's marker.</returns>
    internal (GodotMetadata Metadata, bool Warned) Of(string path, string text)
    {
        if (given is not null)
        {
            return (given, false);
        }
        string? marker = XscnMarker.Read(text);
        if (marker is null)
        {
            return (NewestMetadata(), false);
        }
        if (versions.Carried.Contains(marker, StringComparer.Ordinal))
        {
            return (ForVersion(marker), false);
        }
        GodotMetadata used = NewestMetadata();
        if (!warned.Add(marker))
        {
            return (used, false);
        }
        output.WriteLine($"{path}: warning {CliCodes.MarkerNotCarried}: {XscnMarker.NotCarried(marker, used.Version)}");
        return (used, true);
    }

    /// <summary>
    /// The metadata a file with no marker is read with: the newest version the run carries. Nothing is read from the text,
    /// so a <c>.tscn</c> or <c>.tres</c> source is not parsed as XML to find a marker it cannot have.
    /// </summary>
    internal (GodotMetadata Metadata, bool Warned) Unmarked => (NewestMetadata(), false);

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
        output.WriteLine($"{path}: error {CliCodes.MarkerNotWritten}: {XscnMarker.NotWritten(marker, NewestMetadata().Version)}");
        return false;
    }

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
