using System.Text.RegularExpressions;
using GodotXaml.Core.Metadata;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The name rules hover and completion share: the document's format prefix, a class hint's lists of allowed and excluded
/// classes, and how a type is named in C#.
/// </summary>
internal static partial class NameRules
{
    /// <summary>The prefix a document binds to the format's namespace.</summary>
    /// <param name="text">The document's text.</param>
    /// <returns>The prefix, or null when the document binds none.</returns>
    public static string? FormatPrefix(string text) => FormatDeclaration().Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>
    /// The classes a class name or a hint's list of them allows (<c>Texture2D,-AnimatedTexture</c> allows Texture2D): every
    /// entry without a '-' prefix, in order.
    /// </summary>
    /// <param name="hint">The class name or list.</param>
    /// <returns>The allowed classes' Godot names.</returns>
    public static IEnumerable<string> Allowed(string hint) => Entries(hint).Where(c => !c.StartsWith('-'));

    /// <summary>The classes a hint's list excludes, its entries with a '-' prefix, without the prefix.</summary>
    /// <param name="hint">The class name or list.</param>
    /// <returns>The excluded classes' Godot names.</returns>
    public static IEnumerable<string> Excluded(string hint) => Entries(hint).Where(c => c.StartsWith('-')).Select(c => c[1..]);

    /// <summary>The first class a hint allows, which names the property's type.</summary>
    /// <param name="hint">The class name or list.</param>
    /// <returns>The class's Godot name, or null for an empty hint.</returns>
    public static string? FirstAllowed(string hint) => Allowed(hint).FirstOrDefault();

    /// <summary>
    /// The instantiable classes a hint takes: those that inherit an allowed class, less those that inherit an excluded one.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="hint">The class name or list.</param>
    /// <returns>The classes, in the metadata's order.</returns>
    public static IEnumerable<GodotClassInfo> InstantiableOf(GodotMetadata metadata, string hint)
    {
        List<string> allowed = [.. Allowed(hint)];
        List<string> excluded = [.. Excluded(hint)];
        return metadata.Classes.Where(c =>
            c.Instantiable && allowed.Any(a => metadata.Inherits(c, a)) && !excluded.Any(e => metadata.Inherits(c, e))
        );
    }

    /// <summary>Whether a hint allows a resource: any of its allowed classes inherits Resource.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="hint">The class name or list.</param>
    /// <returns>True when it does.</returns>
    public static bool HoldsResource(GodotMetadata metadata, string hint) =>
        Allowed(hint).Any(a => metadata.FindClass(a) is { } cls && metadata.Inherits(cls, "Resource"));

    /// <summary>A class's C# name by its Godot name; the Godot name when the metadata lacks it.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="godotName">The class's Godot name.</param>
    /// <returns>The name.</returns>
    public static string CSharpClassName(GodotMetadata metadata, string godotName) => metadata.FindClass(godotName)?.CSharpName ?? godotName;

    /// <summary>An Object's class by its C# name (the hint's first allowed class), Variant for a Nil, else the Variant type's name.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="variantType">The Variant type.</param>
    /// <param name="hint">The class name or list of an Object.</param>
    /// <returns>The type's name.</returns>
    public static string TypeName(GodotMetadata metadata, string variantType, string hint) =>
        variantType switch
        {
            "Object" when FirstAllowed(hint) is { } first => CSharpClassName(metadata, first),
            "Nil" => "Variant",
            _ => variantType,
        };

    private static string[] Entries(string hint) => hint.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex("""xmlns:([^\s=:"']+)\s*=\s*["']https://github\.com/leftos/godot-xaml/format/1["']""", RegexOptions.CultureInvariant)]
    private static partial Regex FormatDeclaration();
}
