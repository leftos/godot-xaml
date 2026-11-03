using System.Text.Json;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Values;

// Every property default in metadata/godot-4.7.json is var_to_str of the value (tools/metadata-dump/dump.gd), so each
// one reads and writes back to the same text.
public sealed class MetadataDefaultsTests(MetadataDefaultsTests.MetadataFile file) : IClassFixture<MetadataDefaultsTests.MetadataFile>
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

    // Godot saves a property's getter value, so each class's default for each property it has, own or overridden, is an
    // attribute of that class (XscnValueText.Write gives one only for text that reads back as the same value). A
    // slash-named property (theme_override_colors/…) is dotted or x:Prop by design, and a name with no attribute is x:Prop,
    // so neither is checked.
    [Fact]
    public void WritesEveryDefaultAsAnAttributeOfItsClass()
    {
        GodotMetadata metadata = file.Metadata;
        var names = new XscnPropertyNames(metadata);
        var mismatches = new List<string>();
        int checkedCount = 0;
        int skipped = 0;
        foreach ((string className, string godotName) in PropertiesByClass())
        {
            GodotClassInfo cls = metadata.FindClass(className)!;
            if (metadata.FindDefault(cls, godotName) is not { Value: not GodotNull } found)
            {
                continue;
            }

            if (godotName.Contains('/', StringComparison.Ordinal) || names.AttributeName(cls, godotName) is not { } attributeName)
            {
                skipped++;
                continue;
            }

            checkedCount++;
            XscnValueType type = names.Resolve(cls, attributeName).Type;
            var value = new XscnScalar(found.Value);
            if (XscnValueText.Write(value, type) is not XscnAttributeText)
            {
                mismatches.Add($"{className}.{godotName} ({type.VariantType}): {found.Text}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {checkedCount} defaults ({skipped} skipped) are not attributes:\n{string.Join('\n', mismatches.Take(20))}"
        );
        Assert.NotEqual(0, checkedCount);
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

    private List<string> Defaults()
    {
        var defaults = new List<string>();
        foreach (JsonProperty godotClass in file.Classes.EnumerateObject())
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

    // Each class with the Godot names of its own properties and of the inherited ones it overrides the default of.
    private List<(string Class, string Property)> PropertiesByClass()
    {
        var properties = new List<(string Class, string Property)>();
        foreach (JsonProperty godotClass in file.Classes.EnumerateObject())
        {
            foreach (JsonElement property in godotClass.Value.GetProperty("properties").EnumerateArray())
            {
                properties.Add((godotClass.Name, property.GetProperty("name").GetString()!));
            }

            if (godotClass.Value.TryGetProperty("defaultOverrides", out JsonElement defaults))
            {
                properties.AddRange(defaults.EnumerateObject().Select(d => (godotClass.Name, d.Name)));
            }
        }

        return properties;
    }

    private List<(string Class, string Property, string Text)> DefaultOverrides()
    {
        var overrides = new List<(string Class, string Property, string Text)>();
        foreach (JsonProperty godotClass in file.Classes.EnumerateObject())
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

    /// <summary>metadata/godot-4.7.json, read once for the class: its JSON and the metadata loaded from it.</summary>
    public sealed class MetadataFile : IDisposable
    {
        private readonly JsonDocument json;

        /// <summary>Reads the file.</summary>
        public MetadataFile()
        {
            byte[] bytes = File.ReadAllBytes(RepoFiles.PathOf("metadata", "godot-4.7.json"));
            json = JsonDocument.Parse(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            Metadata = GodotMetadata.Load(stream);
        }

        /// <summary>The file's <c>classes</c> object.</summary>
        public JsonElement Classes => json.RootElement.GetProperty("classes");

        /// <summary>The metadata the file holds.</summary>
        public GodotMetadata Metadata { get; }

        /// <inheritdoc/>
        public void Dispose() => json.Dispose();
    }
}
