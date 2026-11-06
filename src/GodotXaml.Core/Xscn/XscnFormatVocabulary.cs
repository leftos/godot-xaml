using System.Collections.Frozen;

namespace GodotXaml.Core.Xscn;

/// <summary>
/// The format's own names: the <c>x:</c> elements, and the format attributes each kind of element takes. The tree reader
/// reads its tables from here, so what this lists is what the parser accepts.
/// </summary>
public static class XscnFormatVocabulary
{
    /// <summary>The element kind of a Godot class element in a scene: a node.</summary>
    public const string Node = "Node";

    /// <summary>The element kind of a resource element: an <c>.xres</c> root, or the resource a property element holds.</summary>
    public const string Resource = "Resource";

    /// <summary>The element kind of a resource listed in <c>x:Resources</c>, the one element that takes <c>x:Key</c>.</summary>
    public const string TableEntry = "TableEntry";

    /// <summary>The unprefixed attribute that sets an element's script, on every element that takes properties.</summary>
    public const string Script = "Script";

    /// <summary>The local names of the <c>x:</c> attributes of a node, an instance or an override.</summary>
    internal static readonly string[] NodeAttributes =
    [
        "Editable",
        "Groups",
        "Index",
        "InstancePlaceholder",
        "NodePaths",
        "Owner",
        "OwnerUidPath",
        "ParentIdPath",
        "UniqueId",
    ];

    /// <summary>The attributes of <c>x:Connect</c>, all unprefixed.</summary>
    internal static readonly string[] ConnectAttributes =
    [
        "Signal",
        "GodotSignal",
        "To",
        "Method",
        "Flags",
        "Binds",
        "Unbinds",
        "FromUidPath",
        "ToUidPath",
    ];

    /// <summary>The attributes of <c>x:Prop</c>, both unprefixed.</summary>
    internal static readonly string[] PropAttributes = ["Name", "Value"];

    /// <summary>The attributes of <c>x:Uid</c>, both unprefixed.</summary>
    internal static readonly string[] UidAttributes = ["Path", "Value"];

    /// <summary>The unprefixed attributes the format reserves on a node, an instance and an override, which are not properties.</summary>
    internal static readonly (string Kind, string Name)[] SpecialAttributes =
    [
        (Node, "Name"),
        ("Instance", "Name"),
        ("Instance", "Scene"),
        ("Override", "Path"),
    ];

    /// <summary>The local name of the <c>x:</c> attribute that keys a table entry.</summary>
    internal const string Key = "Key";

    /// <summary>
    /// The local name of the <c>x:</c> attribute that marks a document's root with the Godot version whose metadata wrote it
    /// (<c>x:Godot="4.7"</c>).
    /// </summary>
    internal const string Godot = "Godot";

    private static readonly string[] RootAttributeNames = ["x:" + Godot];

    private static readonly string[] ElementNames = ["Uids", "Uid", "Resources", "Scene", "Instance", "Override", "Connect", "Prop"];

    // Godot reads a scene's own properties and a scene inside a PackedScene from 4.8 on.
    private static readonly Version ScenesSince = new(4, 8);

    private static readonly FrozenDictionary<string, IReadOnlyList<string>> AttributesByKind = new Dictionary<string, IReadOnlyList<string>>(
        StringComparer.Ordinal
    )
    {
        [Node] = NodeKind(Node),
        ["Instance"] = NodeKind("Instance"),
        ["Override"] = NodeKind("Override"),
        [Resource] = Array.AsReadOnly<string>([Script]),
        [TableEntry] = Array.AsReadOnly<string>([Script, "x:" + Key]),
        ["Uids"] = Array.AsReadOnly<string>([]),
        ["Uid"] = Array.AsReadOnly(UidAttributes),
        ["Resources"] = Array.AsReadOnly<string>([]),
        ["Scene"] = Array.AsReadOnly<string>([Script]),
        ["Connect"] = Array.AsReadOnly(ConnectAttributes),
        ["Prop"] = Array.AsReadOnly(PropAttributes),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The local names of the <c>x:</c> elements, without the prefix.</summary>
    public static IReadOnlyList<string> Elements { get; } = Array.AsReadOnly(ElementNames);

    /// <summary>
    /// The format attributes only a document's root takes (a scene's root node, an <c>.xres</c>'s root resource), besides
    /// those of its kind, prefixed <c>x:</c>; on any other element they are unknown.
    /// </summary>
    public static IReadOnlyList<string> RootAttributes { get; } = Array.AsReadOnly(RootAttributeNames);

    /// <summary>
    /// The format attributes an element of a kind takes: its reserved unprefixed ones, then its <c>x:</c> ones, prefixed <c>x:</c>.
    /// </summary>
    /// <param name="elementKind">
    /// <see cref="Node"/>, <see cref="Resource"/>, <see cref="TableEntry"/>, or an <c>x:</c> element's local name
    /// (<c>Instance</c>, <c>Override</c>, …).
    /// </param>
    /// <returns>The attribute names, as written with the conventional <c>x</c> prefix; empty for an element that takes none.</returns>
    /// <exception cref="ArgumentException"><paramref name="elementKind"/> names no kind of element.</exception>
    public static IReadOnlyList<string> AttributesFor(string elementKind)
    {
        ArgumentNullException.ThrowIfNull(elementKind);
        return AttributesByKind.TryGetValue(elementKind, out IReadOnlyList<string>? attributes)
            ? attributes
            : throw new ArgumentException(
                $"'{elementKind}' is not an element kind: use Node, Resource, TableEntry or an x: element's name.",
                nameof(elementKind)
            );
    }

    /// <summary>
    /// Whether the Godot version of a metadata reads <c>x:Scene</c> and a scene inside a <c>PackedScene</c>: 4.8 or later.
    /// A version that does not parse is taken as reading them.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <returns>True for 4.8 or later.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is null.</exception>
    public static bool ReadsScenes(GodotXaml.Core.Metadata.GodotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return !System.Version.TryParse(metadata.Version, out Version? version) || version >= ScenesSince;
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> NodeKind(string kind) =>
        Array.AsReadOnly<string>([
            .. SpecialAttributes.Where(a => a.Kind == kind).Select(a => a.Name),
            Script,
            .. NodeAttributes.Select(n => "x:" + n),
        ]);
}
