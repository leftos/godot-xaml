using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotXaml.LanguageServer;

/// <summary>Loads the class reference of a Godot minor version.</summary>
/// <param name="godotMinor">The Godot minor version (<c>4.7</c>).</param>
/// <returns>The class reference, or null when there is none for the version.</returns>
internal delegate ClassDocs? DocsLoader(string godotMinor);

/// <summary>
/// A Godot version's class reference, as <c>tools/class-docs</c> writes it to <c>metadata/docs/godot-&lt;minor&gt;.json</c>:
/// each class's descriptions and its members', signals' and constants' descriptions, in Markdown, keyed by Godot name.
/// </summary>
internal sealed class ClassDocs
{
    private readonly Dictionary<string, ClassDoc> classes;

    private ClassDocs(Dictionary<string, ClassDoc> classes) => this.classes = classes;

    /// <summary>The class reference the server carries for a Godot minor version.</summary>
    /// <param name="godotMinor">The Godot minor version (<c>4.7</c>).</param>
    /// <returns>The class reference, or null when the server carries none for the version.</returns>
    public static ClassDocs? LoadBundled(string godotMinor)
    {
        using Stream? stream = typeof(ClassDocs).Assembly.GetManifestResourceStream($"docs/godot-{godotMinor}.json");
        return stream is null ? null : Read(stream);
    }

    /// <summary>Reads a class reference file.</summary>
    /// <param name="stream">The file's JSON.</param>
    /// <returns>The class reference.</returns>
    /// <exception cref="InvalidDataException">The file holds no classes.</exception>
    public static ClassDocs Read(Stream stream)
    {
        ClassDocsFile? file = JsonSerializer.Deserialize(stream, ClassDocsJsonContext.Default.ClassDocsFile);
        return new ClassDocs(file?.Classes ?? throw new InvalidDataException("The class reference file has no classes object."));
    }

    /// <summary>A class's entry.</summary>
    /// <param name="godotName">The class's Godot name, or <c>@GlobalScope</c> for the global constants.</param>
    /// <returns>The entry, or null when the file has none.</returns>
    public ClassDoc? Class(string godotName) => classes.GetValueOrDefault(godotName);
}

/// <summary>A class's entry in the class reference.</summary>
/// <param name="Brief">The brief description.</param>
/// <param name="Description">The full description.</param>
/// <param name="Members">The properties, by Godot name.</param>
/// <param name="Signals">The signals, by Godot name.</param>
/// <param name="Constants">The constants, enum members among them, by Godot name.</param>
internal sealed record ClassDoc(
    string? Brief,
    string? Description,
    Dictionary<string, MemberDoc>? Members,
    Dictionary<string, MemberDoc>? Signals,
    Dictionary<string, MemberDoc>? Constants
);

/// <summary>A member's, signal's or constant's entry in the class reference.</summary>
/// <param name="Description">The description; empty when the reference gives none.</param>
internal sealed record MemberDoc(string? Description);

/// <summary>The class reference file's top level; its other fields are not read.</summary>
/// <param name="Classes">The classes, by Godot name.</param>
internal sealed record ClassDocsFile(Dictionary<string, ClassDoc>? Classes);

/// <summary>The source-generated JSON metadata of the class reference file, so a NativeAOT build reads it without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClassDocsFile))]
internal sealed partial class ClassDocsJsonContext : JsonSerializerContext;
