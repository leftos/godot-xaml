using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Names;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Names;

// Every class and global enum in metadata/godot-4.7.json, named by GodotEnumNames through GodotMetadata, against the enum
// GodotSharp 4.7.2 compiles, read from the NuGet package's DLL with System.Reflection.Metadata (never loaded or run).
public sealed class GodotSharpEnumTests
{
    // An editor class's C# API is generated into GodotSharpEditor (bindings_generator.cpp, generate_cs_editor_project),
    // not GodotSharp, so GodotSharp has no type to check its enums against.
    private const string EditorClass = "an editor class: GodotSharpEditor holds it, not GodotSharp";

    // Enums whose members cannot match GodotSharp's, each with the reason.
    private static readonly Dictionary<string, string> Exclusions = new(StringComparer.Ordinal)
    {
        ["EditorContextMenuPlugin.ContextMenuSlot"] = EditorClass,
        ["EditorDock.DockLayout"] = EditorClass,
        ["EditorDock.DockSlot"] = EditorClass,
        ["EditorExportPlatform.DebugFlags"] = EditorClass,
        ["EditorExportPlatform.ExportMessageType"] = EditorClass,
        ["EditorExportPreset.ExportFilter"] = EditorClass,
        ["EditorExportPreset.FileExportMode"] = EditorClass,
        ["EditorExportPreset.ScriptExportMode"] = EditorClass,
        ["EditorFeatureProfile.Feature"] = EditorClass,
        ["EditorPlugin.AfterGUIInput"] = EditorClass,
        ["EditorPlugin.CustomControlContainer"] = EditorClass,
        ["EditorPlugin.DockSlot"] = EditorClass,
        ["EditorSceneFormatImporter.ImportFlags"] = EditorClass,
        ["EditorScenePostImportPlugin.InternalImportCategory"] = EditorClass,
        ["EditorSpinSlider.ControlState"] = EditorClass,
        ["EditorToaster.Severity"] = EditorClass,
        ["EditorUndoRedoManager.SpecialHistory"] = EditorClass,
        ["EditorVCSInterface.ChangeType"] = EditorClass,
        ["EditorVCSInterface.TreeArea"] = EditorClass,
    };

    [Fact]
    public void NamesEveryEnumMemberAsGodotSharpDoes()
    {
        var metadata = GodotMetadata.Load(MetadataPath);
        Dictionary<string, List<(string Name, long Value)>> godotSharp = GodotSharpEnums.Read(GodotSharpPath());
        var failures = new List<string>();
        int checkedCount = 0;
        foreach ((string qualifiedName, string[] typeNames) in EnumsToCheck(metadata))
        {
            string? failure = Compare(metadata.FindEnum(qualifiedName)!, typeNames, godotSharp);
            if (Exclusions.ContainsKey(qualifiedName))
            {
                failures.AddRange(failure is null ? [$"{qualifiedName}: excluded but matches GodotSharp"] : []);
                continue;
            }

            failures.AddRange(failure is null ? [] : [failure]);
            checkedCount++;
        }

        Assert.True(failures.Count == 0, $"{failures.Count} enums differ from GodotSharp:\n{string.Join('\n', failures)}");
        Assert.True(checkedCount > 0, "No enum was checked.");
    }

    private static string MetadataPath => RepoFiles.PathOf("metadata", "godot-4.7.json");

    private static string GodotSharpPath() =>
        typeof(GodotSharpEnumTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "GodotSharpPath").Value!;

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

    // The enums an assembly defines, keyed by full name with nested types joined by '.', each with its literal fields.
    private static class GodotSharpEnums
    {
        public static Dictionary<string, List<(string Name, long Value)>> Read(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var enums = new Dictionary<string, List<(string Name, long Value)>>(StringComparer.Ordinal);
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                if (IsEnum(reader, type))
                {
                    enums[FullName(reader, type)] = Members(reader, type);
                }
            }

            return enums;
        }

        private static bool IsEnum(MetadataReader reader, TypeDefinition type)
        {
            if (type.BaseType.Kind != HandleKind.TypeReference)
            {
                return false;
            }

            TypeReference baseType = reader.GetTypeReference((TypeReferenceHandle)type.BaseType);
            return reader.StringComparer.Equals(baseType.Namespace, "System") && reader.StringComparer.Equals(baseType.Name, "Enum");
        }

        private static string FullName(MetadataReader reader, TypeDefinition type)
        {
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            string prefix = declaring.IsNil ? reader.GetString(type.Namespace) : FullName(reader, reader.GetTypeDefinition(declaring));
            return $"{prefix}.{reader.GetString(type.Name)}";
        }

        private static List<(string Name, long Value)> Members(MetadataReader reader, TypeDefinition type)
        {
            var members = new List<(string, long)>();
            foreach (FieldDefinitionHandle handle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(handle);
                if ((field.Attributes & FieldAttributes.Literal) != 0)
                {
                    members.Add((reader.GetString(field.Name), ConstantValue(reader, reader.GetConstant(field.GetDefaultValue()))));
                }
            }

            return members;
        }

        private static long ConstantValue(MetadataReader reader, Constant constant)
        {
            BlobReader blob = reader.GetBlobReader(constant.Value);
            return constant.TypeCode switch
            {
                ConstantTypeCode.SByte => blob.ReadSByte(),
                ConstantTypeCode.Byte => blob.ReadByte(),
                ConstantTypeCode.Int16 => blob.ReadInt16(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                ConstantTypeCode.Int32 => blob.ReadInt32(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                ConstantTypeCode.Int64 => blob.ReadInt64(),
                ConstantTypeCode.UInt64 => (long)blob.ReadUInt64(),
                _ => throw new InvalidDataException($"An enum constant of type {constant.TypeCode}."),
            };
        }
    }
}
