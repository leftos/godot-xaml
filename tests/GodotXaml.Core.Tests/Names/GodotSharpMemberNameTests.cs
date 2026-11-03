using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Names;

// Every class, property and signal name metadata/godot-4.7.json carries, named by GodotMetadata through GodotNames, against
// the C# names Godot 4.7.2 generates into GodotSharp and GodotSharpEditor, read from the NuGet packages' DLLs with
// System.Reflection.Metadata (never loaded or run).
public sealed class GodotSharpMemberNameTests
{
    private const string Absent = "absent";

    // PROPERTY_USAGE_ARRAY (core/object/property_info.h), the usage of an inspector grouping of an array's elements.
    private const int ArrayUsage = 1 << 18;

    // The classes Godot's C# API leaves out. bindings_generator.cpp's _populate_object_type_interfaces skips a class
    // ClassDB::is_class_exposed reports as not exposed, and a GDCLASS type the engine instantiates without a public
    // registration stays unexposed, so neither assembly carries a C# type for it or its members.
    private static readonly HashSet<string> UnexposedClasses =
    [
        "GDScriptEditorTranslationParserPlugin",
        "GDScriptFunctionState",
        "GDScriptNativeClass",
        "GLTFDocumentExtensionPhysics",
        "GLTFDocumentExtensionTextureKTX",
        "GLTFDocumentExtensionTextureWebP",
        "GodotNavigationServer2D",
        "GodotPhysicsServer2D",
        "GodotPhysicsServer3D",
        "GodotSharp",
        "IPWindows",
        "ManagedCallableMiddleman",
        "MovieWriterMJPEG",
        "MovieWriterOGV",
        "MovieWriterPNGWAV",
        "ResourceFormatImporterSaver",
        "SceneCacheInterface",
        "SceneRPCInterface",
        "SceneReplicationInterface",
        "ThemeContext",
    ];

    [Fact]
    public void NamesEveryPropertyAsGodotSharpDoes()
    {
        var metadata = GodotMetadata.Load(MetadataPath);
        Dictionary<string, Dictionary<string, string>> assemblies = MemberNames();
        var failures = new List<string>();
        int checkedCount = 0;
        foreach (GodotClassInfo cls in metadata.Classes)
        {
            if (UnexposedClasses.Contains(cls.Name))
            {
                continue;
            }

            string typeName = $"Godot.{cls.CSharpName}.PropertyName";
            Dictionary<string, string> pairs = PairsOf(assemblies, typeName);
            foreach (GodotPropertyInfo property in cls.Properties)
            {
                if (IsInspectorArrayGroup(property))
                {
                    continue;
                }

                string found = pairs.Remove(property.Name, out string? csName) ? csName : Absent;
                checkedCount++;
                if (found != property.CSharpName)
                {
                    failures.Add($"{typeName}.{property.Name}: expected {property.CSharpName}, found {found}");
                }
            }

            failures.AddRange(pairs.Select(pair => $"{typeName}.{pair.Key}: expected absent (not in the metadata), found {pair.Value}"));
        }

        Assert.True(failures.Count == 0, $"{failures.Count} properties differ from GodotSharp:\n{string.Join('\n', failures)}");
        Assert.True(checkedCount > 0, "No property was checked.");
    }

    [Fact]
    public void NamesEverySignalAsGodotSharpDoes()
    {
        var metadata = GodotMetadata.Load(MetadataPath);
        Dictionary<string, Dictionary<string, string>> assemblies = MemberNames();
        var failures = new List<string>();
        int checkedCount = 0;
        foreach (GodotClassInfo cls in metadata.Classes)
        {
            if (UnexposedClasses.Contains(cls.Name))
            {
                continue;
            }

            string typeName = $"Godot.{cls.CSharpName}.SignalName";
            Dictionary<string, string> pairs = PairsOf(assemblies, typeName);
            foreach (GodotSignalInfo signal in cls.Signals)
            {
                checkedCount++;
                bool present = pairs.Remove(signal.Name, out string? csName);
                string found = present ? csName! : Absent;
                if (signal.CSharpName is null)
                {
                    if (present)
                    {
                        failures.Add(
                            $"{typeName}.{signal.Name}: expected absent (the C# API leaves out a signal whose name begins with '_'), found {found}"
                        );
                    }

                    continue;
                }

                if (found != signal.CSharpName)
                {
                    failures.Add($"{typeName}.{signal.Name}: expected {signal.CSharpName}, found {found}");
                }
            }

            failures.AddRange(pairs.Select(pair => $"{typeName}.{pair.Key}: expected absent (not in the metadata), found {pair.Value}"));
        }

        Assert.True(failures.Count == 0, $"{failures.Count} signals differ from GodotSharp:\n{string.Join('\n', failures)}");
        Assert.True(checkedCount > 0, "No signal was checked.");
    }

    [Fact]
    public void NamesEveryClassAsGodotSharpDoes()
    {
        var metadata = GodotMetadata.Load(MetadataPath);
        HashSet<string> typeNames = TypeNames();
        var failures = new List<string>();
        int checkedCount = 0;
        foreach (GodotClassInfo cls in metadata.Classes)
        {
            if (UnexposedClasses.Contains(cls.Name))
            {
                continue;
            }

            checkedCount++;
            string typeName = $"Godot.{cls.CSharpName}";
            if (!typeNames.Contains(typeName))
            {
                failures.Add($"{cls.Name}: expected {typeName}, absent from both GodotSharp and GodotSharpEditor");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} classes differ from GodotSharp:\n{string.Join('\n', failures)}");
        Assert.True(checkedCount > 0, "No class was checked.");
    }

    private static string MetadataPath => RepoFiles.PathOf("metadata", "godot-4.7.json");

    // bindings_generator.cpp, _populate_object_type_interfaces skips a property of Variant::NIL whose usage has
    // PROPERTY_USAGE_ARRAY: an inspector grouping of an array's elements, which has no C# name.
    private static bool IsInspectorArrayGroup(GodotPropertyInfo property) => property.Type == "Nil" && (property.Usage & ArrayUsage) != 0;

    // Both assemblies' name proxies, keyed by full type name; neither assembly defines a type the other does.
    private static Dictionary<string, Dictionary<string, string>> MemberNames()
    {
        var names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string path in GodotSharpAssembly.Paths)
        {
            foreach ((string typeName, List<(string GodotName, string CSharpName)> pairs) in GodotSharpAssembly.MemberNamePairs(path))
            {
                if (!names.TryAdd(typeName, PairMap(typeName, pairs)))
                {
                    throw new InvalidOperationException($"Both GodotSharp and GodotSharpEditor define the type '{typeName}'.");
                }
            }
        }

        return names;
    }

    private static Dictionary<string, string> PairMap(string typeName, List<(string GodotName, string CSharpName)> pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string godotName, string csName) in pairs)
        {
            if (!map.TryAdd(godotName, csName))
            {
                throw new InvalidOperationException($"GodotSharp's '{typeName}' holds the field for '{godotName}' twice.");
            }
        }

        return map;
    }

    // A copy, because a fact removes each pair it accounts for and reads the rest as names the metadata lacks.
    private static Dictionary<string, string> PairsOf(Dictionary<string, Dictionary<string, string>> names, string typeName) =>
        names.TryGetValue(typeName, out Dictionary<string, string>? pairs)
            ? new Dictionary<string, string>(pairs, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    private static HashSet<string> TypeNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in GodotSharpAssembly.Paths)
        {
            names.UnionWith(GodotSharpAssembly.TypeNames(path));
        }

        return names;
    }
}
