using System.Text;
using System.Text.Json.Nodes;
using GodotXaml.Core.Metadata;

namespace GodotXaml.LanguageServer.Tests;

/// <summary>
/// The metadata a test server carries: the bundled 4.7 and a hand-made 4.8, its <c>version</c> renamed and <c>Label</c>'s
/// <c>text</c> property removed, so a file read with one of them is told apart by a diagnostic.
/// </summary>
internal static class MetadataSeams
{
    /// <summary>The hand-made 4.8: the bundled 4.7's metadata, its version renamed and Label's text property removed.</summary>
    public static GodotMetadata Version48 { get; } = Load48();

    /// <summary>The loader: 4.7 bundled, 4.8 hand-made, and the newest (4.8) with a warning for any other version.</summary>
    public static MetadataLoader Loader { get; } = Load;

    private static LoadedMetadata Load(string? version) =>
        version switch
        {
            "4.7" => new LoadedMetadata(GodotMetadata.LoadBundled("4.7"), "4.7", null),
            "4.8" => new LoadedMetadata(Version48, "4.8", null),
            _ => new LoadedMetadata(
                Version48,
                "4.8",
                version is null ? "the project names no Godot version; using 4.8" : $"Godot {version} has no bundled metadata; using 4.8"
            ),
        };

    private static GodotMetadata Load48()
    {
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(RepoRoot.PathOf("metadata", "godot-4.7.json")))!.AsObject();
        metadata["version"] = "4.8";
        JsonArray properties = metadata["classes"]!.AsObject()["Label"]!.AsObject()["properties"]!.AsArray();
        for (int index = properties.Count - 1; index >= 0; index--)
        {
            if ((string?)properties[index]!["name"] == "text")
            {
                properties.RemoveAt(index);
            }
        }

        return GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(metadata.ToJsonString())));
    }
}
