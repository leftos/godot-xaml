using System.Text.Json;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// Every property default in metadata/godot-4.7.json is var_to_str of the value (tools/metadata-dump/dump.gd), so each
// one reads and writes back to the same text.
public sealed class MetadataDefaultsTests
{
    // The default texts in metadata/godot-4.7.json: 4,190 properties, 701 of them without a default.
    private const int DefaultCount = 3489;

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

    // A default override is var_to_str of a class's own value for an inherited property too, and its texts include forms no
    // declared default uses (1e-05, Array[Font]([]), mixed untyped arrays), so each one reads and writes back as itself.
    [Fact]
    public void WritesEveryDefaultOverrideBackUnchanged()
    {
        List<(string Class, string Property, string Text)> overrides = DefaultOverrides();
        var mismatches = new List<string>();
        foreach ((string _, string _, string text) in overrides)
        {
            string written = WriteBack(text);
            if (written != text)
            {
                mismatches.Add($"{text} -> {written}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {overrides.Count} overrides differ:\n{string.Join('\n', mismatches.Take(20))}");
        Assert.NotEmpty(overrides);
        Assert.Equal("2", overrides.FirstOrDefault(o => o.Class == "Button" && o.Property == "focus_mode").Text);
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

    private static List<(string Class, string Property, string Text)> DefaultOverrides()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(RepoFiles.PathOf("metadata", "godot-4.7.json")));
        var overrides = new List<(string Class, string Property, string Text)>();
        foreach (JsonProperty godotClass in metadata.RootElement.GetProperty("classes").EnumerateObject())
        {
            if (!godotClass.Value.TryGetProperty("defaultOverrides", out JsonElement defaults))
            {
                continue;
            }

            foreach (JsonProperty property in defaults.EnumerateObject())
            {
                overrides.Add((godotClass.Name, property.Name, property.Value.GetString()!));
            }
        }

        return overrides;
    }
}
