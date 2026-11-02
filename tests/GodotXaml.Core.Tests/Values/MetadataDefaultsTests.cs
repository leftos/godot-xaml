using System.Text.Json;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// Every property default in metadata/godot-4.7.json is var_to_str of the value (tools/metadata-dump/dump.gd), so each
// one in a form the reader handles reads and writes back to the same text.
public sealed class MetadataDefaultsTests
{
    // The forms the reader rejects: containers, typed containers and packed arrays.
    private static readonly string[] UnhandledPrefixes = ["[", "{", "Array[", "Dictionary[", "Packed"];

    [Fact]
    public void WritesEveryScalarDefaultBackUnchanged()
    {
        List<string> defaults = ScalarDefaults();
        var mismatches = new List<string>();
        foreach (string text in defaults)
        {
            string written = WriteBack(text);
            if (written != text)
            {
                mismatches.Add($"{text} -> {written}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {defaults.Count} defaults differ:\n{string.Join('\n', mismatches.Take(20))}");
        Assert.True(defaults.Count > 3000, $"{defaults.Count} scalar defaults read and written back unchanged");
    }

    private static string WriteBack(string text)
    {
        try
        {
            return GodotValueWriter.Write(GodotValueReader.Read(text), ValueWriteOptions.Default);
        }
        catch (GodotValueParseException error)
        {
            return error.Message;
        }
    }

    private static List<string> ScalarDefaults()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(RepoFiles.PathOf("metadata", "godot-4.7.json")));
        var defaults = new List<string>();
        foreach (JsonProperty godotClass in metadata.RootElement.GetProperty("classes").EnumerateObject())
        {
            foreach (JsonElement property in godotClass.Value.GetProperty("properties").EnumerateArray())
            {
                if (property.TryGetProperty("default", out JsonElement value) && IsScalar(value.GetString()!))
                {
                    defaults.Add(value.GetString()!);
                }
            }
        }

        return defaults;
    }

    private static bool IsScalar(string text) => !UnhandledPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
}
