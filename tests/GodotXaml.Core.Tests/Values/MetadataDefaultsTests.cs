using System.Text.Json;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// Every property default in metadata/godot-4.7.json is var_to_str of the value (tools/metadata-dump/dump.gd), so each
// one reads and writes back to the same text.
public sealed class MetadataDefaultsTests
{
    // The default texts in metadata/godot-4.7.json: 4,190 properties, 484 of them without a default.
    private const int DefaultCount = 3706;

    [Fact]
    public void WritesEveryDefaultBackUnchanged()
    {
        List<string> defaults = Defaults();
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
        Assert.Equal(DefaultCount, defaults.Count);
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

    private static List<string> Defaults()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(RepoFiles.PathOf("metadata", "godot-4.7.json")));
        var defaults = new List<string>();
        foreach (JsonProperty godotClass in metadata.RootElement.GetProperty("classes").EnumerateObject())
        {
            foreach (JsonElement property in godotClass.Value.GetProperty("properties").EnumerateArray())
            {
                if (property.TryGetProperty("default", out JsonElement value))
                {
                    defaults.Add(value.GetString()!);
                }
            }
        }

        return defaults;
    }
}
