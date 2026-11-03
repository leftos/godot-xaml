using GodotXaml.Core.Metadata;

namespace GodotXaml.Cli;

/// <summary>The Godot metadata file the parser reads: the one given, else the one shipped beside the tool.</summary>
internal static class MetadataFile
{
    /// <summary>Where the metadata file sits beside the tool's assembly.</summary>
    internal static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "metadata", "godot-4.7.json");

    /// <summary>Loads the metadata, reporting why it could not be loaded.</summary>
    /// <param name="givenPath">The <c>--metadata</c> file, or null for the shipped one.</param>
    /// <param name="error">Where a failure is reported, naming the path and the reason.</param>
    /// <returns>The metadata, or null when it could not be loaded.</returns>
    internal static GodotMetadata? TryLoad(string? givenPath, TextWriter error)
    {
        string path = givenPath ?? Shipped;
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
