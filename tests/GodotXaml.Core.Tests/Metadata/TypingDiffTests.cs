using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Metadata;

public sealed class TypingDiffTests
{
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void ConsecutiveBundledVersionsHaveTheirChangesListed()
    {
        Assert.True(
            GodotMetadata.BundledVersions.Count >= 1,
            "This test compares consecutive bundled metadata versions; the assembly carries none, so there is nothing to compare."
        );
        IReadOnlyList<string> versions = [.. GodotMetadata.BundledVersions.Order(StringComparer.Ordinal)];
        for (int i = 1; i < versions.Count; i++)
        {
            ImmutableArray<TypingChange> changes = TypingDiff.Between(
                GodotMetadata.LoadBundled(versions[i - 1]),
                GodotMetadata.LoadBundled(versions[i])
            );

            Assert.Equal(
                changes.OrderBy(c => c.Class, StringComparer.Ordinal).ThenBy(c => c.Property, StringComparer.Ordinal).ThenBy(c => c.Kind),
                changes
            );
        }
    }

    [Fact]
    public void TheSameMetadataHasNoTypingChanges() => Assert.Empty(TypingDiff.Between(Real, Real));

    // AStar3D gains a property whose C# name Control's custom_minimum_size already uses, with another type.
    [Fact]
    public void ACSharpNameThatBecomesAmbiguousIsListed()
    {
        const string className = "AStar3D";
        const string propertyName = "custom_minimum_size";
        const string propertyType = "int";
        JsonObject metadata = MetadataJson();
        Properties(metadata, className)
            .Add(
                JsonNode.Parse(
                    $$"""
                    {"name": "{{propertyName}}", "type": "{{propertyType}}", "enumType": null, "bitfield": false,
                     "hint": 0, "hintString": "", "className": "", "usage": 6}
                    """
                )
            );

        TypingChange[] expected =
        [
            new("", "CustomMinimumSize", TypingChangeKind.CSharpNameAgreement, "custom_minimum_size: Vector2", "custom_minimum_size: unknown"),
        ];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    // Control.MouseFilter renames one member and renumbers another; Control.mouse_filter is its only user.
    [Fact]
    public void AChangedEnumIsListed()
    {
        const string className = "Control";
        const string propertyName = "mouse_filter";
        const string enumName = "MouseFilter";
        const int renamedMember = 1;
        const string newMemberName = "MOUSE_FILTER_HALT";
        const int renumberedMember = 2;
        const int newMemberValue = 5;
        JsonObject metadata = MetadataJson();
        JsonArray values = Class(metadata, className)["enums"]!.AsObject()[enumName]!.AsObject()["values"]!.AsArray();
        values[renamedMember]!.AsArray()[0] = newMemberName;
        values[renumberedMember]!.AsArray()[1] = newMemberValue;

        TypingChange[] expected =
        [
            new(className, propertyName, TypingChangeKind.EnumMembers, "Stop=0, Pass=1, Ignore=2", "Ignore: 2 -> 5; Pass: removed; Halt: added"),
        ];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    // AStar2D loses a property AStar3D keeps with the same C# name, type and Godot name, so no C# name changes meaning.
    [Fact]
    public void ARemovedPropertyIsListed()
    {
        const string className = "AStar2D";
        const string propertyName = "neighbor_filter_enabled";
        JsonObject metadata = MetadataJson();
        JsonArray properties = Properties(metadata, className);
        properties.Remove(properties.First(p => (string?)p!["name"] == propertyName));

        TypingChange[] expected = [new(className, propertyName, TypingChangeKind.PropertyRemoved, "", "")];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    // Node2D.position changes type; the C# name Position is ambiguous across classes already, so its agreement stays unknown.
    [Fact]
    public void ATypeChangeIsListed()
    {
        const string className = "Node2D";
        const string propertyName = "position";
        const string oldType = "Vector2";
        const string newType = "Vector3";
        JsonObject metadata = MetadataJson();
        Property(metadata, className, propertyName)["type"] = newType;

        TypingChange[] expected = [new(className, propertyName, TypingChangeKind.Type, oldType, newType)];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    // Control.custom_minimum_size changes hint; its C# name stays unique and its resolved type does not change.
    [Fact]
    public void AHintChangeIsListed()
    {
        const string className = "Control";
        const string propertyName = "custom_minimum_size";
        const int oldHint = 0;
        const int newHint = 1;
        JsonObject metadata = MetadataJson();
        Property(metadata, className, propertyName)["hint"] = newHint;

        TypingChange[] expected = [new(className, propertyName, TypingChangeKind.Hint, $"{oldHint}", $"{newHint}")];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    // AESContext is a leaf class no other class inherits from; it declares no property, so its removal changes no C# name.
    [Fact]
    public void AClassRemovedIsListed()
    {
        const string className = "AESContext";
        JsonObject metadata = MetadataJson();
        Assert.True(metadata["classes"]!.AsObject().Remove(className));

        TypingChange[] expected = [new(className, "", TypingChangeKind.ClassRemoved, "", "")];

        Assert.Equal(expected, TypingDiff.Between(Real, Load(metadata)));
    }

    private static JsonObject MetadataJson() => JsonNode.Parse(File.ReadAllText(RepoFiles.PathOf("metadata", "godot-4.7.json")))!.AsObject();

    private static JsonObject Class(JsonObject metadata, string className) => metadata["classes"]!.AsObject()[className]!.AsObject();

    private static JsonArray Properties(JsonObject metadata, string className) => Class(metadata, className)["properties"]!.AsArray();

    private static JsonObject Property(JsonObject metadata, string className, string propertyName) =>
        Properties(metadata, className).First(p => (string?)p!["name"] == propertyName)!.AsObject();

    private static GodotMetadata Load(JsonObject metadata) => GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(metadata.ToJsonString())));
}
