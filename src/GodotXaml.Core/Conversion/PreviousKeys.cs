using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The keys a previous <c>.xscn</c> or <c>.xres</c> gives the sub_resources that pair with its <c>x:Resources</c> entries, so
/// a save changes only what changed: first the id its conversion back gives an entry, which a loaded sub_resource keeps
/// through a save, and otherwise the same type and either the same non-empty <c>resource_name</c> or equal properties. A
/// reference to another sub_resource compares as any reference (<c>SubResource("?")</c>) and an ext_resource as its path,
/// so a resource whose only change is which other sub_resource it uses still matches.
/// </summary>
internal static class PreviousKeys
{
    // Godot's own text for a value, so two properties compare however their sources spelled the value.
    private static readonly ValueWriteOptions WriteOptions = new(Compat: false);

    // What every reference to another sub_resource compares as: which one it names is not part of the match.
    private static readonly GodotValue SubResourceReference = new GodotResourceRef(GodotRefKind.Sub, "?");

    private const string ResourceNameProperty = "resource_name";

    /// <summary>The keys of a previous document's entries that match a sub_resource, in table order.</summary>
    /// <param name="sub">The sub_resource that needs a key.</param>
    /// <param name="previous">The document the conversion replaces, or null.</param>
    /// <param name="extPath">The path of an ext_resource, by its id; null when no section has the id.</param>
    /// <returns>The matching entries' keys.</returns>
    public static IEnumerable<string> Keys(TscnSubResource sub, XscnDocument? previous, Func<string, string?> extPath)
    {
        if (previous is null)
        {
            yield break;
        }

        foreach (XscnResource entry in previous.Resources)
        {
            if (entry.Key is { } key && Matches(sub, entry, extPath))
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// The previous document's keyed entries by the id its conversion back gives each, which is the id a loaded
    /// sub_resource keeps through a save. An entry nothing reaches has no id and is left out.
    /// </summary>
    /// <param name="previous">The document the conversion replaces.</param>
    /// <param name="metadata">The engine classes, for the conversion back.</param>
    /// <param name="seed">The seed the host gave the conversion, so the ids come out as its loader minted them.</param>
    /// <returns>Each id the document gives an entry, and that entry.</returns>
    public static IReadOnlyDictionary<string, XscnResource> EntriesById(XscnDocument previous, GodotMetadata metadata, ulong seed)
    {
        if (previous.Resources.Length == 0)
        {
            return new Dictionary<string, XscnResource>();
        }

        // The conversion's issues are ignored: the document was already reported when it was loaded, and an entry nothing
        // reaches has no id and is left out.
        ConversionIds ids = XscnToTscn.Convert(previous, metadata, new ConversionOptions { Seed = seed }).Ids!;
        Dictionary<string, XscnResource> entries = new(StringComparer.Ordinal);
        foreach (XscnResource entry in previous.Resources)
        {
            if (ids.SubIdOf(entry) is { } id)
            {
                entries.TryAdd(id, entry);
            }
        }

        return entries;
    }

    /// <summary>The key of the previous entry a sub_resource pairs with by id: its id names an entry of the same type, else null.</summary>
    /// <param name="sub">The sub_resource that needs a key.</param>
    /// <param name="entries">The previous document's entries by id.</param>
    /// <returns>The entry's key, or null when no entry has the id or the types differ.</returns>
    public static string? KeyById(TscnSubResource sub, IReadOnlyDictionary<string, XscnResource> entries) =>
        entries.TryGetValue(sub.Id, out XscnResource? entry) && TypeName(entry) is { } type && string.Equals(type, sub.Type, StringComparison.Ordinal)
            ? entry.Key
            : null;

    private static bool Matches(TscnSubResource sub, XscnResource entry, Func<string, string?> extPath) =>
        TypeName(entry) is { } type
        && string.Equals(type, sub.Type, StringComparison.Ordinal)
        && (SameResourceName(sub, entry) || SameProperties(sub, entry, extPath));

    private static string? TypeName(XscnResource entry) =>
        entry.Type switch
        {
            XscnClassType known => known.Class.Name,
            XscnUnknownType unknown => unknown.Name,
            _ => null,
        };

    // A resource_name the two set to the same text matches them whatever else changed.
    private static bool SameResourceName(TscnSubResource sub, XscnResource entry) =>
        TscnReferences.ResourceName(sub) is { } name && string.Equals(name, ResourceName(entry), StringComparison.Ordinal);

    private static string? ResourceName(XscnResource entry) =>
        entry.Properties.LastOrDefault(property => property.GodotName == ResourceNameProperty)?.Value
            is XscnScalar { Value: GodotString { Value.Length: > 0 } name }
            ? name.Value
            : null;

    private static bool SameProperties(TscnSubResource sub, XscnResource entry, Func<string, string?> extPath)
    {
        List<(string Name, string Text)> theirs = [];
        foreach (XscnProperty property in entry.Properties)
        {
            if (EntryText(property.Value) is not { } text)
            {
                return false;
            }

            theirs.Add((property.GodotName, text));
        }

        List<(string Name, string Text)> ours = [.. sub.Properties.Select(property => (property.Name, SourceText(property.Value, extPath)))];
        return Sorted(theirs).SequenceEqual(Sorted(ours));
    }

    private static List<(string Name, string Text)> Sorted(IEnumerable<(string Name, string Text)> properties) =>
        [.. properties.OrderBy(property => property.Name, StringComparer.Ordinal)];

    // One property's text on the .tscn side: its references are mapped first, so one to a sub_resource compares as any
    // other and one to an ext_resource as that file's path.
    private static string SourceText(GodotValue value, Func<string, string?> extPath) =>
        GodotValueWriter.Write(ValueReferences.Map(value, reference => SourceReference(reference, extPath)), WriteOptions);

    private static GodotValue SourceReference(GodotValue reference, Func<string, string?> extPath) =>
        reference switch
        {
            GodotResourceRef { Kind: GodotRefKind.Sub } => SubResourceReference,
            GodotResourceRef { Kind: GodotRefKind.External, Id: var id } => extPath(id) is { } path ? new GodotPathRef(null, path) : reference,
            _ => reference,
        };

    // One property's text on the .xscn side; null for a value that is not one of its property's type, which never
    // matches.
    private static string? EntryText(XscnValue value) =>
        value switch
        {
            XscnScalar scalar => GodotValueWriter.Write(ValueReferences.Map(scalar.Value, KeyReference), WriteOptions),
            XscnFile file => GodotValueWriter.Write(new GodotPathRef(null, file.Path), WriteOptions),
            XscnKeyRef or XscnInline => GodotValueWriter.Write(SubResourceReference, WriteOptions),
            _ => null,
        };

    // A {Key} names an entry of the previous document, which the sub_resource it matches is not yet; the .xscn side has
    // no ext_resource table, so an ExtResource reference stays as it stands.
    private static GodotValue KeyReference(GodotValue reference) =>
        reference is GodotResourceRef { Kind: GodotRefKind.Sub } ? SubResourceReference : reference;
}
