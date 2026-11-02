using System.Text.Json;

namespace GodotXaml.Cli;

/// <summary>
/// The repo's <c>godot-xaml.json</c>: the first one found from the working directory up to the filesystem root. Its one
/// setting is <c>{"convert": {"overwrite": true}}</c>, which lets <c>convert</c> replace an output that exists.
/// </summary>
internal static class ProjectConfig
{
    /// <summary>The config file's name.</summary>
    internal const string FileName = "godot-xaml.json";

    /// <summary>Reads whether <c>convert</c> may overwrite, reporting a config file that cannot be used.</summary>
    /// <param name="workingDirectory">The folder the search starts from.</param>
    /// <param name="error">Where a usage error is reported, naming the file and the key.</param>
    /// <returns>The setting, false when no file or no setting is found, or null when the file cannot be used.</returns>
    internal static bool? TryReadOverwrite(string workingDirectory, TextWriter error)
    {
        string? path = Find(workingDirectory);
        if (path is null)
        {
            return false;
        }
        string? text = SourceText.Read(path, error);
        if (text is null)
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            return OverwriteOf(document.RootElement, path, error);
        }
        catch (JsonException exception)
        {
            error.WriteLine($"{path}: not JSON: {exception.Message.TrimEnd('.')}.");
            return null;
        }
    }

    private static string? Find(string workingDirectory)
    {
        for (DirectoryInfo? folder = new(Path.GetFullPath(workingDirectory)); folder is not null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool? OverwriteOf(JsonElement root, string path, TextWriter error)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            error.WriteLine($"{path}: the top level must be an object.");
            return null;
        }
        bool overwrite = false;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name != "convert")
            {
                error.WriteLine($"{path}: unknown key '{property.Name}'.");
                return null;
            }
            bool? read = ConvertOverwriteOf(property.Value, path, error);
            if (read is null)
            {
                return null;
            }
            overwrite = read.Value;
        }
        return overwrite;
    }

    private static bool? ConvertOverwriteOf(JsonElement convert, string path, TextWriter error)
    {
        if (convert.ValueKind != JsonValueKind.Object)
        {
            error.WriteLine($"{path}: 'convert' must be an object.");
            return null;
        }
        bool overwrite = false;
        foreach (JsonProperty property in convert.EnumerateObject())
        {
            if (property.Name != "overwrite")
            {
                error.WriteLine($"{path}: unknown key 'convert.{property.Name}'.");
                return null;
            }
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error.WriteLine($"{path}: 'convert.overwrite' must be true or false.");
                return null;
            }
            overwrite = property.Value.GetBoolean();
        }
        return overwrite;
    }
}
