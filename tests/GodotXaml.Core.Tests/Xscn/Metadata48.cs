using System.Text;
using System.Text.Json.Nodes;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Xscn;

/// <summary>A hand-made Godot 4.8 metadata: the bundled 4.7's, its <c>version</c> renamed, for the constructs only 4.8 reads.</summary>
internal static class Metadata48
{
    /// <summary>The hand-made 4.8.</summary>
    public static GodotMetadata Version48 { get; } = Load48();

    private static GodotMetadata Load48()
    {
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(RepoFiles.PathOf("metadata", "godot-4.7.json")))!.AsObject();
        metadata["version"] = "4.8";
        return GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(metadata.ToJsonString())));
    }
}
