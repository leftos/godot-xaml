using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using GodotXaml.Core.Names;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Metadata;

/// <summary>
/// The engine's classes, properties, enums and theme overrides, loaded from a metadata file
/// (<c>metadata/godot-4.7.json</c>, written by tools/metadata-dump). Immutable once loaded, so one instance can be shared
/// across threads.
/// </summary>
public sealed class GodotMetadata
{
    // The name an embedded metadata file has in this assembly: GodotXaml.Core.Metadata.godot-4.7.json.
    private const string BundledResourcePrefix = "GodotXaml.Core.Metadata.godot-";

    private const string BundledResourceSuffix = ".json";

    private readonly FrozenDictionary<string, GodotClassInfo> classes;
    private readonly FrozenDictionary<string, GodotClassInfo> classesByCSharpName;
    private readonly FrozenDictionary<string, GodotEnumInfo> globalEnums;

    private GodotMetadata(List<GodotClassInfo> classList, ImmutableArray<GodotEnumInfo> globalEnumList)
    {
        classes = Index(classList, c => c.Name, "class");
        classesByCSharpName = Index(classList, c => c.CSharpName, "class C# name");
        globalEnums = Index(globalEnumList, e => e.QualifiedName, "global enum");
        foreach (GodotClassInfo cls in classList)
        {
            CheckParentChain(cls);
        }
    }

    /// <summary>Loads a metadata file.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The file's classes and enums.</returns>
    /// <exception cref="InvalidDataException">
    /// The file is not JSON or does not hold what a metadata file holds; the message names the path and what is wrong.
    /// </exception>
    public static GodotMetadata Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        try
        {
            return Load(stream);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException($"{path}: {error.Message}", error);
        }
    }

    /// <summary>Loads metadata from a stream holding a metadata file's JSON.</summary>
    /// <param name="stream">The UTF-8 JSON.</param>
    /// <returns>The file's classes and enums.</returns>
    /// <exception cref="InvalidDataException">
    /// The stream is not JSON or does not hold what a metadata file holds; the message names what is wrong.
    /// </exception>
    public static GodotMetadata Load(Stream stream)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"The metadata is not JSON: {error.Message}", error);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The metadata's top level is not a JSON object.");
            }

            ImmutableArray<GodotEnumInfo> globals = GodotMetadataReader.GlobalEnums(JsonFields.Object(root, "globalEnums", "The metadata"));
            List<GodotClassInfo> classList = GodotMetadataReader.Classes(JsonFields.Object(root, "classes", "The metadata"));
            return new GodotMetadata(classList, globals);
        }
    }

    /// <summary>The Godot versions whose metadata this assembly carries, by minor version (<c>4.7</c>).</summary>
    public static IReadOnlyList<string> BundledVersions { get; } =
    [
        .. typeof(GodotMetadata)
            .Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(BundledResourcePrefix, StringComparison.Ordinal) && n.EndsWith(BundledResourceSuffix, StringComparison.Ordinal))
            .Select(n => n[BundledResourcePrefix.Length..^BundledResourceSuffix.Length])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Loads the metadata of a Godot version this assembly carries.</summary>
    /// <param name="godotMinor">The engine's minor version (<c>4.7</c>).</param>
    /// <returns>The version's classes and enums.</returns>
    /// <exception cref="ArgumentException">
    /// The assembly carries no metadata for <paramref name="godotMinor"/>; the message names the versions it carries.
    /// </exception>
    public static GodotMetadata LoadBundled(string godotMinor)
    {
        string name = BundledResourcePrefix + godotMinor + BundledResourceSuffix;
        using Stream? stream = typeof(GodotMetadata).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            string carried = BundledVersions.Count == 0 ? "none" : string.Join(", ", BundledVersions.Select(v => $"'{v}'"));
            throw new ArgumentException($"This assembly carries no metadata for Godot '{godotMinor}'; it carries {carried}.", nameof(godotMinor));
        }

        return Load(stream);
    }

    /// <summary>
    /// Loads the bundled metadata for a project's Godot version, or the newest bundled version when the assembly does
    /// not carry the project's or the project names none.
    /// </summary>
    /// <param name="projectVersion">The project's Godot minor version (<c>4.7</c>), or null when it names none.</param>
    /// <param name="version">The version whose metadata was loaded.</param>
    /// <param name="warning">Why the newest version stands in for the project's, or null when the project's was loaded.</param>
    /// <returns>The loaded version's classes and enums.</returns>
    /// <exception cref="InvalidOperationException">The assembly carries no metadata at all.</exception>
    public static GodotMetadata LoadBundledForProject(string? projectVersion, out string version, out string? warning)
    {
        if (projectVersion is not null && BundledVersions.Contains(projectVersion, StringComparer.Ordinal))
        {
            version = projectVersion;
            warning = null;
            return LoadBundled(version);
        }

        version =
            BundledVersions.Where(v => Version.TryParse(v, out _)).MaxBy(v => Version.Parse(v))
            ?? throw new InvalidOperationException("This assembly carries no metadata.");
        warning = projectVersion is null
            ? $"the project names no Godot version; using {version}"
            : $"Godot {projectVersion} has no bundled metadata; using {version}";
        return LoadBundled(version);
    }

    /// <summary>Every class in the file, in no set order.</summary>
    public IEnumerable<GodotClassInfo> Classes => classes.Values;

    /// <summary>The class with a Godot name.</summary>
    /// <param name="godotName">The class's Godot name (<c>CPUParticles2D</c>).</param>
    /// <returns>The class, or null when the file has no such class.</returns>
    public GodotClassInfo? FindClass(string godotName) => classes.GetValueOrDefault(godotName);

    /// <summary>The class with a C# name.</summary>
    /// <param name="csName">The class's C# name (<c>CpuParticles2D</c>).</param>
    /// <returns>The class, or null when the file has no such class.</returns>
    public GodotClassInfo? FindClassByCSharpName(string csName) => classesByCSharpName.GetValueOrDefault(csName);

    /// <summary>Whether a class is a given class or inherits from it, as ClassDB::is_parent_class answers.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="ancestorGodotName">The other class's Godot name.</param>
    /// <returns>Whether <paramref name="cls"/> is that class or one of its descendants.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public bool Inherits(GodotClassInfo cls, string ancestorGodotName) => Chain(Resolve(cls)).Any(c => c.Name == ancestorGodotName);

    /// <summary>A class and its ancestors, as this metadata has them.</summary>
    /// <param name="cls">The class to start from.</param>
    /// <returns>The class, then each ancestor up to <c>Object</c>.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    internal IEnumerable<GodotClassInfo> ChainOf(GodotClassInfo cls) => Chain(Resolve(cls));

    /// <summary>Every class from <paramref name="cls"/> up its parents to <c>Object</c>.</summary>
    /// <param name="cls">The class to start from.</param>
    /// <returns>The class itself, then each ancestor, nearest first.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public IEnumerable<GodotClassInfo> AncestorsOf(GodotClassInfo cls) => ChainOf(cls);

    /// <summary>A property of a class or of one of its ancestors, by its Godot name.</summary>
    /// <param name="cls">The class to start from.</param>
    /// <param name="godotName">The property's Godot name (<c>custom_minimum_size</c>).</param>
    /// <returns>The nearest class's property of that name, or null when none has it.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public GodotPropertyInfo? FindProperty(GodotClassInfo cls, string godotName) =>
        Chain(Resolve(cls)).Select(c => c.OwnProperty(godotName)).FirstOrDefault(p => p is not null);

    /// <summary>A property of a class or of one of its ancestors, by its C# name.</summary>
    /// <param name="cls">The class to start from.</param>
    /// <param name="csName">The property's C# name (<c>CustomMinimumSize</c>).</param>
    /// <returns>The nearest class's property of that name, or null when none has it.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public GodotPropertyInfo? FindPropertyByCSharpName(GodotClassInfo cls, string csName) =>
        Chain(Resolve(cls)).Select(c => c.OwnPropertyByCSharpName(csName)).FirstOrDefault(p => p is not null);

    /// <summary>
    /// Every property a class has, its own first in ClassDB order, then each ancestor's in that order, nearest first; a name
    /// that a nearer class declares again hides the farther one.
    /// </summary>
    /// <param name="cls">The class.</param>
    /// <returns>Each property once, as the nearest class that declares it.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public IEnumerable<GodotPropertyInfo> PropertiesOf(GodotClassInfo cls)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (GodotClassInfo current in Chain(Resolve(cls)))
        {
            foreach (GodotPropertyInfo property in current.Properties)
            {
                if (names.Add(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    /// <summary>
    /// A property's default for a class, as Godot reads it for that class (its own value, else the nearest override or
    /// declaration up the chain); a class Godot keeps no defaults for has none for any property.
    /// </summary>
    /// <param name="cls">The class.</param>
    /// <param name="godotName">The property's Godot name (<c>focus_mode</c>).</param>
    /// <returns>The default, or null when the class has none for the property.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public GodotPropertyDefault? FindDefault(GodotClassInfo cls, string godotName)
    {
        foreach (GodotClassInfo current in Chain(Resolve(cls)))
        {
            if (!current.HasDefaults)
            {
                return null;
            }

            GodotPropertyInfo? property = current.OwnProperty(godotName);
            if (property is not null)
            {
                return property.DefaultText is null ? null : new GodotPropertyDefault(property.DefaultText, property.Default!);
            }

            GodotPropertyDefault? overrideDefault = current.DefaultOverride(godotName);
            if (overrideDefault is not null)
            {
                return overrideDefault;
            }
        }

        return null;
    }

    /// <summary>A signal of a class or of one of its ancestors, by its C# name.</summary>
    /// <param name="cls">The class to start from.</param>
    /// <param name="csName">The signal's C# name (<c>Pressed</c>).</param>
    /// <returns>The nearest class's signal of that name, or null when none has it.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public GodotSignalInfo? FindSignalByCSharpName(GodotClassInfo cls, string csName) =>
        Chain(Resolve(cls)).SelectMany(c => c.Signals).FirstOrDefault(s => s.CSharpName == csName);

    /// <summary>
    /// Every signal a class has that C# can name, its own first in the file's order, then each ancestor's in that order,
    /// nearest first; a name that a nearer class declares again hides the farther one.
    /// </summary>
    /// <param name="cls">The class.</param>
    /// <returns>Each signal once, as the nearest class that declares it, leaving out a signal whose C# name is null.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public IEnumerable<GodotSignalInfo> SignalsOf(GodotClassInfo cls)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (GodotClassInfo current in Chain(Resolve(cls)))
        {
            foreach (GodotSignalInfo signal in current.Signals)
            {
                if (signal.CSharpName is not null && names.Add(signal.Name))
                {
                    yield return signal;
                }
            }
        }
    }

    /// <summary>An enum by the name a property's <c>enumType</c> gives.</summary>
    /// <param name="enumType">A class enum's <c>Class.Enum</c> name (the class's ancestors are searched too), or a global enum's name.</param>
    /// <returns>The enum, or null when neither a class nor the global enums have it (<c>Vector3.Axis</c>).</returns>
    public GodotEnumInfo? FindEnum(string enumType)
    {
        int dot = enumType.LastIndexOf('.');
        GodotClassInfo? owner = dot > 0 ? FindClass(enumType[..dot]) : null;
        GodotEnumInfo? classEnum = owner is null
            ? null
            : Chain(owner).Select(c => c.OwnEnum(enumType[(dot + 1)..])).FirstOrDefault(e => e is not null);
        return classEnum ?? globalEnums.GetValueOrDefault(enumType);
    }

    /// <summary>A theme override item of a class, by its property name.</summary>
    /// <param name="cls">The class; its list already holds the items it inherits.</param>
    /// <param name="godotName">The item's property name (<c>theme_override_colors/font_color</c>).</param>
    /// <returns>The item, or null when the class has no such item.</returns>
    /// <exception cref="ArgumentException">This metadata has no class of <paramref name="cls"/>'s name.</exception>
    public GodotThemeOverrideInfo? FindThemeOverride(GodotClassInfo cls, string godotName) => Resolve(cls).ThemeOverride(godotName);

    private static FrozenDictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key, string what) =>
        UniqueIndex.Build(items, key, duplicate => $"The metadata lists the {what} '{duplicate}' twice.");

    // A class given to a lookup may come from another GodotMetadata; it is used by its name, as this metadata has it.
    private GodotClassInfo Resolve(GodotClassInfo cls) =>
        FindClass(cls.Name) ?? throw new ArgumentException($"This metadata has no class '{cls.Name}'.", nameof(cls));

    // Every parent is a class in the file, and following parents ends at a class without one.
    private void CheckParentChain(GodotClassInfo cls)
    {
        GodotClassInfo current = cls;
        for (int steps = 0; current.Parent is not null; steps++)
        {
            if (steps == classes.Count)
            {
                throw new InvalidDataException($"The parent chain of class '{cls.Name}' loops.");
            }

            current =
                classes.GetValueOrDefault(current.Parent)
                ?? throw new InvalidDataException(
                    $"Class '{current.Name}' names the parent '{current.Parent}', which is not a class in the metadata."
                );
        }
    }

    // The class, then each ancestor up to Object; the constructor has checked that every parent exists.
    private IEnumerable<GodotClassInfo> Chain(GodotClassInfo cls)
    {
        for (GodotClassInfo? current = cls; current is not null; current = current.Parent is null ? null : classes[current.Parent])
        {
            yield return current;
        }
    }
}

/// <summary>Builds the info records from a metadata file's JSON, in the format tools/metadata-dump/README.md gives.</summary>
internal static class GodotMetadataReader
{
    /// <summary>The global enums, keyed by name in <paramref name="globalEnums"/>.</summary>
    public static ImmutableArray<GodotEnumInfo> GlobalEnums(JsonElement globalEnums) =>
        [
            .. globalEnums
                .EnumerateObject()
                .Select(e => Enum(e.Name, e.Value, $"Global enum '{e.Name}'", names => GodotEnumNames.GlobalEnumMembers(e.Name, names))),
        ];

    /// <summary>The classes, keyed by Godot name in <paramref name="classes"/>.</summary>
    public static List<GodotClassInfo> Classes(JsonElement classes) => [.. classes.EnumerateObject().Select(c => Class(c.Name, c.Value))];

    private static GodotClassInfo Class(string name, JsonElement entry)
    {
        string where = $"Class '{name}'";
        string csName = GodotNames.PascalToPascalCase(name);
        ImmutableArray<GodotPropertyInfo> properties =
        [
            .. JsonFields.Array(entry, "properties", where).EnumerateArray().Select(p => Property(name, csName, p)),
        ];
        return new GodotClassInfo(
            new GodotClassParts(
                name,
                csName,
                JsonFields.NullableString(entry, "parent", where),
                JsonFields.Bool(entry, "instantiable", where),
                JsonFields.OptionalBool(entry, "hasDefaults", where) ?? true,
                properties,
                ListOrder(entry, properties, where),
                DefaultOverrides(entry, name, where),
                [.. JsonFields.Object(entry, "enums", where).EnumerateObject().Select(e => ClassEnum(name, e))],
                [.. JsonFields.OptionalArray(entry, "themeOverrides", where).Select(t => ThemeOverride(name, t))],
                [.. JsonFields.Array(entry, "signals", where).EnumerateArray().Select(s => Signal(name, csName, properties, s))]
            )
        );
    }

    // The names the class declares in ClassDB order, slash-named ones included: listOrder where the dump wrote one (a class
    // with a slash-named name), else the names of its properties.
    private static ImmutableArray<string> ListOrder(JsonElement entry, ImmutableArray<GodotPropertyInfo> properties, string where) =>
        entry.TryGetProperty("listOrder", out _)
            ?
            [
                .. JsonFields
                    .Array(entry, "listOrder", where)
                    .EnumerateArray()
                    .Select(n =>
                        n.ValueKind == JsonValueKind.String
                            ? n.GetString()!
                            : throw new InvalidDataException($"{where}: 'listOrder' holds an item that is not a string.")
                    ),
            ]
            : [.. properties.Select(p => p.Name)];

    private static GodotPropertyInfo Property(string className, string classCSharpName, JsonElement entry)
    {
        string name = JsonFields.String(entry, "name", $"A property of class '{className}'");
        string where = $"Property {className}.{name}";
        string? defaultText = entry.TryGetProperty("default", out _) ? JsonFields.String(entry, "default", where) : null;
        return new GodotPropertyInfo(
            name,
            MemberCSharpName(name, classCSharpName),
            JsonFields.String(entry, "type", where),
            JsonFields.NullableString(entry, "enumType", where),
            JsonFields.Bool(entry, "bitfield", where),
            JsonFields.Int(entry, "hint", where),
            JsonFields.String(entry, "hintString", where),
            JsonFields.Int(entry, "usage", where),
            JsonFields.String(entry, "className", where),
            defaultText,
            defaultText is null ? null : ReadDefault(defaultText, where),
            entry.TryGetProperty("getterType", out _) ? JsonFields.String(entry, "getterType", where) : null
        );
    }

    /// <summary>The defaults a class gives the properties it inherits, keyed by property name.</summary>
    private static ImmutableArray<KeyValuePair<string, GodotPropertyDefault>> DefaultOverrides(JsonElement entry, string className, string where) =>
        [
            .. JsonFields
                .OptionalObject(entry, "defaultOverrides", where)
                .Select(o => new KeyValuePair<string, GodotPropertyDefault>(o.Name, DefaultOverride(className, o.Name, o.Value))),
        ];

    private static GodotPropertyDefault DefaultOverride(string className, string propertyName, JsonElement text)
    {
        string where = $"The default override of {className}.{propertyName}";
        if (text.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"{where} is not a string.");
        }

        string value = text.GetString()!;
        return new GodotPropertyDefault(value, ReadDefault(value, where));
    }

    private static GodotValue ReadDefault(string text, string where)
    {
        try
        {
            return GodotValueReader.Read(text);
        }
        catch (GodotValueParseException error)
        {
            throw new InvalidDataException($"{where} has a default that is not Godot value text ({text}): {error.Message}", error);
        }
    }

    // bindings_generator.cpp, where it builds property and signal proxies: snake_to_pascal_case of the name, with '_'
    // appended when that equals the enclosing class's proxy name.
    private static string MemberCSharpName(string godotName, string classCSharpName)
    {
        string csName = GodotNames.SnakeToPascalCase(godotName, inputIsUpper: false);
        return csName == classCSharpName ? csName + "_" : csName;
    }

    private static GodotEnumInfo ClassEnum(string className, JsonProperty entry)
    {
        string qualifiedName = $"{className}.{entry.Name}";
        return Enum(qualifiedName, entry.Value, $"Enum {qualifiedName}", GodotEnumNames.ClassEnumMembers);
    }

    private static GodotEnumInfo Enum(
        string qualifiedName,
        JsonElement entry,
        string where,
        Func<IReadOnlyList<string>, ImmutableArray<string>> csNames
    )
    {
        List<(string Name, long Value)> values = [.. JsonFields.Array(entry, "values", where).EnumerateArray().Select(v => EnumValue(v, where))];
        ImmutableArray<string> names = csNames([.. values.Select(v => v.Name)]);
        return new GodotEnumInfo(
            qualifiedName,
            JsonFields.Bool(entry, "bitfield", where),
            [.. values.Select((v, i) => new GodotEnumMember(v.Name, names[i], v.Value))]
        );
    }

    private static (string Name, long Value) EnumValue(JsonElement pair, string where)
    {
        if (
            pair.ValueKind != JsonValueKind.Array
            || pair.GetArrayLength() != 2
            || pair[0].ValueKind != JsonValueKind.String
            || pair[1].ValueKind != JsonValueKind.Number
            || !pair[1].TryGetInt64(out long value)
        )
        {
            throw new InvalidDataException($"{where} has a value that is not a [name, integer] pair: {pair.GetRawText()}");
        }

        return (pair[0].GetString()!, value);
    }

    private static GodotThemeOverrideInfo ThemeOverride(string className, JsonElement entry)
    {
        string where = $"A theme override of class '{className}'";
        return new GodotThemeOverrideInfo(
            JsonFields.String(entry, "name", where),
            JsonFields.String(entry, "type", where),
            JsonFields.Int(entry, "hint", where),
            JsonFields.String(entry, "hintString", where)
        );
    }

    private static GodotSignalInfo Signal(string className, string classCSharpName, ImmutableArray<GodotPropertyInfo> properties, JsonElement entry)
    {
        string name = JsonFields.String(entry, "name", $"A signal of class '{className}'");
        string where = $"Signal {className}.{name}";
        return new GodotSignalInfo(
            name,
            SignalCSharpName(name, classCSharpName, properties),
            [.. JsonFields.Array(entry, "args", where).EnumerateArray().Select(a => SignalArgument(a, where))]
        );
    }

    // bindings_generator.cpp's signal loop: a signal whose name begins with '_' is skipped ("Signals starting with an
    // underscore are internal and not meant to be exposed"), so it has no C# name. Otherwise the member rule, then 'Signal'
    // appended when the name equals a property's proxy name of the same class (the generator also checks methods, which
    // the metadata does not list).
    private static string? SignalCSharpName(string godotName, string classCSharpName, ImmutableArray<GodotPropertyInfo> properties)
    {
        if (godotName.StartsWith('_'))
        {
            return null;
        }

        string csName = MemberCSharpName(godotName, classCSharpName);
        return properties.Any(p => p.CSharpName == csName) ? csName + "Signal" : csName;
    }

    private static GodotSignalArgument SignalArgument(JsonElement entry, string where) =>
        new(
            JsonFields.String(entry, "name", $"An argument of {where}"),
            JsonFields.String(entry, "type", $"An argument of {where}"),
            JsonFields.String(entry, "className", $"An argument of {where}")
        );
}

/// <summary>Typed reads of a JSON object's members, failing with a message naming the object and the key.</summary>
internal static class JsonFields
{
    public static JsonElement Object(JsonElement owner, string key, string where) => OfKind(owner, key, where, JsonValueKind.Object);

    public static JsonElement Array(JsonElement owner, string key, string where) => OfKind(owner, key, where, JsonValueKind.Array);

    /// <summary>The array's items, or none when the key is absent.</summary>
    public static IEnumerable<JsonElement> OptionalArray(JsonElement owner, string key, string where) =>
        owner.TryGetProperty(key, out _) ? Array(owner, key, where).EnumerateArray() : [];

    /// <summary>The object's members, or none when the key is absent.</summary>
    public static IEnumerable<JsonProperty> OptionalObject(JsonElement owner, string key, string where) =>
        owner.TryGetProperty(key, out _) ? Object(owner, key, where).EnumerateObject() : [];

    /// <summary>The Boolean, or null when the key is absent.</summary>
    public static bool? OptionalBool(JsonElement owner, string key, string where) =>
        owner.TryGetProperty(key, out _) ? Bool(owner, key, where) : null;

    public static string String(JsonElement owner, string key, string where) => OfKind(owner, key, where, JsonValueKind.String).GetString()!;

    public static string? NullableString(JsonElement owner, string key, string where) =>
        Required(owner, key, where).ValueKind == JsonValueKind.Null ? null : String(owner, key, where);

    public static bool Bool(JsonElement owner, string key, string where) =>
        Required(owner, key, where).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw WrongKind(key, where, "boolean"),
        };

    public static int Int(JsonElement owner, string key, string where) =>
        OfKind(owner, key, where, JsonValueKind.Number).TryGetInt32(out int value) ? value : throw WrongKind(key, where, "32-bit integer");

    private static JsonElement OfKind(JsonElement owner, string key, string where, JsonValueKind kind)
    {
        JsonElement value = Required(owner, key, where);
        return value.ValueKind == kind ? value : throw WrongKind(key, where, kind.ToString().ToLowerInvariant());
    }

    private static JsonElement Required(JsonElement owner, string key, string where) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(key, out JsonElement value)
            ? value
            : throw new InvalidDataException($"{where} has no '{key}'.");

    private static InvalidDataException WrongKind(string key, string where, string expected) => new($"{where}: '{key}' is not a {expected}.");
}
