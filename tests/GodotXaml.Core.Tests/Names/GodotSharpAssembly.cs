using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace GodotXaml.Core.Tests.Names;

/// <summary>
/// Reads the C# API Godot 4.7.2 generates out of the NuGet packages' DLLs with System.Reflection.Metadata: the assemblies are
/// never loaded or run, so no Godot static constructor runs.
/// </summary>
internal static class GodotSharpAssembly
{
    // ECMA-335's single-byte opcodes: the instructions a name proxy's static constructor holds, and ret.
    private const byte OpRet = 0x2A;
    private const byte OpLdstr = 0x72;
    private const byte OpCall = 0x28;
    private const byte OpStsfld = 0x80;

    /// <summary>The GodotSharp.dll path, from the test project's assembly metadata.</summary>
    public static string GodotSharpPath => PathOf("GodotSharpPath");

    /// <summary>The GodotSharpEditor.dll path, from the test project's assembly metadata.</summary>
    public static string GodotSharpEditorPath => PathOf("GodotSharpEditorPath");

    /// <summary>Both packages' DLL paths: GodotSharp's, then GodotSharpEditor's.</summary>
    public static IReadOnlyList<string> Paths => [GodotSharpPath, GodotSharpEditorPath];

    /// <summary>The enums an assembly defines, keyed by full name with nested types joined by '.', each with its literal fields.</summary>
    public static Dictionary<string, List<(string Name, long Value)>> EnumMembers(string path) =>
        Read(
            path,
            (reader, _) =>
            {
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
        );

    /// <summary>
    /// The nested <c>PropertyName</c> and <c>SignalName</c> types an assembly defines, keyed by full name, each with its
    /// (Godot name, C# field name) pairs.
    /// </summary>
    public static Dictionary<string, List<(string GodotName, string CSharpName)>> MemberNamePairs(string path) =>
        Read(
            path,
            (reader, pe) =>
            {
                var pairs = new Dictionary<string, List<(string GodotName, string CSharpName)>>(StringComparer.Ordinal);
                foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                {
                    TypeDefinition type = reader.GetTypeDefinition(handle);
                    if (!type.GetDeclaringType().IsNil && IsNameProxy(reader, type))
                    {
                        pairs[FullName(reader, type)] = NamePairs(reader, pe, type);
                    }
                }

                return pairs;
            }
        );

    /// <summary>The full names of every type an assembly defines, nested types joined by '.'.</summary>
    public static HashSet<string> TypeNames(string path) =>
        Read<HashSet<string>>(path, (reader, _) => [.. reader.TypeDefinitions.Select(handle => FullName(reader, reader.GetTypeDefinition(handle)))]);

    private static string PathOf(string key) =>
        typeof(GodotSharpAssembly).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    private static TResult Read<TResult>(string path, Func<MetadataReader, PEReader, TResult> read)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return read(pe.GetMetadataReader(), pe);
    }

    // bindings_generator.cpp generates a nested type per class for each kind of C# name, its fields standing for the
    // members' Godot names.
    private static bool IsNameProxy(MetadataReader reader, TypeDefinition type) =>
        reader.StringComparer.Equals(type.Name, "PropertyName") || reader.StringComparer.Equals(type.Name, "SignalName");

    private static List<(string GodotName, string CSharpName)> NamePairs(MetadataReader reader, PEReader pe, TypeDefinition type)
    {
        string typeName = FullName(reader, type);
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (reader.StringComparer.Equals(method.Name, ".cctor"))
            {
                return NamePairs(reader, pe, typeName, method);
            }
        }

        return type.GetFields().Count == 0 ? [] : throw new InvalidDataException($"Nested type '{typeName}' has fields but no static constructor.");
    }

    // Each field is set as ldstr "<godot name>", call (the StringName conversion) and stsfld <field>; any other shape leaves
    // the pairing unknowable, so it throws rather than pair by field order.
    private static List<(string GodotName, string CSharpName)> NamePairs(MetadataReader reader, PEReader pe, string typeName, MethodDefinition method)
    {
        ImmutableArray<byte> il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent();
        var pairs = new List<(string GodotName, string CSharpName)>();
        int offset = 0;
        while (offset < il.Length)
        {
            if (il[offset] == OpRet)
            {
                offset++;
                continue;
            }

            if (il[offset] != OpLdstr || offset + 15 > il.Length || il[offset + 5] != OpCall || il[offset + 10] != OpStsfld)
            {
                throw new InvalidDataException(
                    $"The static constructor of '{typeName}' has an opcode sequence this reader cannot pair at IL offset {offset}."
                );
            }

            uint stringToken = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(offset + 1, 4));
            uint fieldToken = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(offset + 11, 4));
            string godotName = reader.GetUserString(MetadataTokens.UserStringHandle((int)(stringToken & 0xFFFFFF)));
            pairs.Add((godotName, FieldName(reader, typeName, fieldToken)));
            offset += 15;
        }

        return pairs;
    }

    private static string FieldName(MetadataReader reader, string typeName, uint fieldToken)
    {
        if ((fieldToken >> 24) != (uint)HandleKind.FieldDefinition)
        {
            throw new InvalidDataException($"The static constructor of '{typeName}' stores to a token that is not a field.");
        }

        return reader.GetString(reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle((int)(fieldToken & 0xFFFFFF))).Name);
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
