using System.Text.Json;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Names;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Names;

// Every class and global enum in metadata/godot-4.7.json, named by GodotEnumNames through GodotMetadata, against the enum
// GodotSharp 4.7.2 compiles, read from the NuGet package's DLL with System.Reflection.Metadata (never loaded or run).
public sealed class GodotSharpEnumTests
{
    [Fact]
    public void NamesEveryEnumMemberAsGodotSharpDoes()
    {
        var metadata = GodotMetadata.Load(MetadataPath);
        Dictionary<string, List<(string Name, long Value)>> godotSharp = EnumMembers();
        var failures = new List<string>();
        int checkedCount = 0;
        foreach ((string qualifiedName, string[] typeNames) in EnumsToCheck(metadata))
        {
            string? failure = Compare(metadata.FindEnum(qualifiedName)!, typeNames, godotSharp);
            failures.AddRange(failure is null ? [] : [failure]);
            checkedCount++;
        }

        Assert.True(failures.Count == 0, $"{failures.Count} enums differ from GodotSharp:\n{string.Join('\n', failures)}");
        Assert.True(checkedCount > 0, "No enum was checked.");
    }

    private static string MetadataPath => RepoFiles.PathOf("metadata", "godot-4.7.json");

    // The enums both assemblies define: Godot's C# API splits into GodotSharp (core classes) and GodotSharpEditor (editor
    // classes, bindings_generator.cpp generate_cs_editor_project), and neither assembly defines a type the other does.
    private static Dictionary<string, List<(string Name, long Value)>> EnumMembers()
    {
        var enums = new Dictionary<string, List<(string Name, long Value)>>(StringComparer.Ordinal);
        foreach (string path in GodotSharpAssembly.Paths)
        {
            foreach ((string name, List<(string Name, long Value)> members) in GodotSharpAssembly.EnumMembers(path))
            {
                if (!enums.TryAdd(name, members))
                {
                    throw new InvalidOperationException($"Both GodotSharp and GodotSharpEditor define the enum '{name}'.");
                }
            }
        }

        return enums;
    }

    private static string? Compare(GodotEnumInfo info, string[] typeNames, Dictionary<string, List<(string Name, long Value)>> godotSharp)
    {
        string? typeName = typeNames.FirstOrDefault(godotSharp.ContainsKey);
        if (typeName is null)
        {
            return $"{info.QualifiedName}: no enum {string.Join(" or ", typeNames)} in GodotSharp";
        }

        string[] ours = Sorted(info.Members.Select(m => (m.CSharpName, m.Value)));
        string[] theirs = Sorted(godotSharp[typeName]);
        string onlyOurs = string.Join(", ", ours.Except(theirs));
        string onlyTheirs = string.Join(", ", theirs.Except(ours));
        return ours.SequenceEqual(theirs) ? null : $"{info.QualifiedName} ({typeName}): ours [{onlyOurs}], GodotSharp's [{onlyTheirs}]";
    }

    private static string[] Sorted(IEnumerable<(string Name, long Value)> members) =>
        [.. members.Select(m => $"{m.Name}={m.Value}").Order(StringComparer.Ordinal)];

    // Each enum's qualified name with the GodotSharp types it may be: Godot.<Class>.<Enum>, or <Enum>Enum where the enum
    // clashes with a member (bindings_generator.cpp, where it populates enums); Godot.<Enum> for a global.
    private static List<(string QualifiedName, string[] TypeNames)> EnumsToCheck(GodotMetadata metadata)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(MetadataPath));
        var found = new List<(string QualifiedName, string[] TypeNames)>();
        foreach (JsonProperty godotClass in json.RootElement.GetProperty("classes").EnumerateObject())
        {
            string csClass = metadata.FindClass(godotClass.Name)!.CSharpName;
            foreach (JsonProperty godotEnum in godotClass.Value.GetProperty("enums").EnumerateObject())
            {
                string csEnum = $"Godot.{csClass}.{GodotNames.PascalToPascalCase(godotEnum.Name)}";
                found.Add(($"{godotClass.Name}.{godotEnum.Name}", [csEnum, csEnum + "Enum"]));
            }
        }

        foreach (JsonProperty globalEnum in json.RootElement.GetProperty("globalEnums").EnumerateObject())
        {
            string csEnum = string.Join('.', globalEnum.Name.Split('.').Select(GodotNames.PascalToPascalCase));
            found.Add((globalEnum.Name, [$"Godot.{csEnum}"]));
        }

        return found;
    }
}
