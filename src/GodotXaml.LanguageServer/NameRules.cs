using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The name rules hover and completion share: the property an attribute names, a class hint's lists of allowed and
/// excluded classes, and how a type is named in C#.
/// </summary>
internal static class NameRules
{
    /// <summary>
    /// The property a class tag's attribute names: the attribute's C# name on the tag's class, PackedScene's on
    /// <c>x:Scene</c>. Any other format or scene tag, a property element, an unknown class, a prefixed attribute, a signal
    /// and an unknown name have none.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="context">A context on the attribute's name or value.</param>
    /// <returns>The property, or null.</returns>
    public static GodotPropertyInfo? PropertyOf(GodotMetadata metadata, XscnCursorContext context)
    {
        if (context.AttributeName is not { } name || name.Contains(':', StringComparison.Ordinal) || AttributeOwner(context) is not { } cls)
        {
            return null;
        }

        return metadata.FindPropertyByCSharpName(cls, name);
    }

    /// <summary>
    /// The class whose properties an element's start tag holds: a class tag's class, or PackedScene for <c>x:Scene</c> (the
    /// context resolves a prefixed tag to a class only there); null for any other element.
    /// </summary>
    /// <param name="context">A context on or in the element's start tag.</param>
    /// <returns>The class, or null.</returns>
    public static GodotClassInfo? AttributeOwner(XscnCursorContext context) =>
        context.ElementName is { } element
        && !element.Contains('.', StringComparison.Ordinal)
        && (context.ElementPrefix is null || element == "Scene")
            ? context.Class
            : null;

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
        return metadata.Classes.Where(c => c.Instantiable && Takes(metadata, allowed, excluded, c));
    }

    /// <summary>Whether a hint takes a class: it inherits an allowed class and no excluded one.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="hint">The class name or list.</param>
    /// <param name="cls">The class.</param>
    /// <returns>True when the hint takes it.</returns>
    public static bool Takes(GodotMetadata metadata, string hint, GodotClassInfo cls) =>
        Takes(metadata, [.. Allowed(hint)], [.. Excluded(hint)], cls);

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

    private static bool Takes(GodotMetadata metadata, List<string> allowed, List<string> excluded, GodotClassInfo cls) =>
        allowed.Any(a => metadata.Inherits(cls, a)) && !excluded.Any(e => metadata.Inherits(cls, e));

    private static string[] Entries(string hint) => hint.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
