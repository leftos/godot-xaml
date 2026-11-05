using System.Collections.Immutable;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Xscn;

/// <summary>Whether a document is a scene (<c>.xscn</c>) or a resource (<c>.xres</c>).</summary>
public enum XscnDocumentKind
{
    /// <summary>A <c>.xscn</c> scene, its root a node.</summary>
    Scene,

    /// <summary>A <c>.xres</c> resource, its root a resource.</summary>
    Resource,
}

/// <summary>
/// A parsed <c>.xscn</c> or <c>.xres</c> file. The model is immutable; its records hold arrays, so two models are not
/// promised to compare equal by content.
/// </summary>
/// <param name="Kind">Scene or resource.</param>
/// <param name="Namespaces">The scene namespaces the root declares, in source order.</param>
/// <param name="Uids">The <c>&lt;x:Uids&gt;</c> table, in source order.</param>
/// <param name="Resources">The <c>&lt;x:Resources&gt;</c> table, in source order.</param>
/// <param name="Root">The root: an <see cref="XscnNode"/> for a scene, an <see cref="XscnResource"/> for a resource.</param>
/// <param name="Godot">
/// The root's <c>x:Godot</c> marker, the Godot version whose metadata the file was written with (<c>4.7</c>); null when the
/// file has none. The writer stamps the version of the metadata it writes with, not this.
/// </param>
public sealed record XscnDocument(
    XscnDocumentKind Kind,
    ImmutableArray<XscnNamespace> Namespaces,
    ImmutableArray<XscnUid> Uids,
    ImmutableArray<XscnResource> Resources,
    XscnElement Root,
    string? Godot
)
{
    /// <summary>The scene namespaces the root declares, in source order.</summary>
    public ImmutableArray<XscnNamespace> Namespaces
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Namespaces));
    } = GodotValueTables.Required(Namespaces, nameof(Namespaces));

    /// <summary>The <c>&lt;x:Uids&gt;</c> table, in source order.</summary>
    public ImmutableArray<XscnUid> Uids
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Uids));
    } = GodotValueTables.Required(Uids, nameof(Uids));

    /// <summary>The <c>&lt;x:Resources&gt;</c> table, in source order.</summary>
    public ImmutableArray<XscnResource> Resources
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Resources));
    } = GodotValueTables.Required(Resources, nameof(Resources));

    /// <summary>The text of each comment just before the <c>&lt;x:Uids&gt;</c> start tag, in source order.</summary>
    public ImmutableArray<string> UidsLeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(UidsLeadingComments));
    } = [];

    /// <summary>The text of each comment after the last <c>&lt;x:Uid&gt;</c> and before the table's end tag, in source order.</summary>
    public ImmutableArray<string> UidsTrailingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(UidsTrailingComments));
    } = [];

    /// <summary>The text of each comment just before the <c>&lt;x:Resources&gt;</c> start tag, in source order.</summary>
    public ImmutableArray<string> ResourcesLeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(ResourcesLeadingComments));
    } = [];

    /// <summary>The text of each comment after the last resource of <c>&lt;x:Resources&gt;</c> and before its end tag, in source order.</summary>
    public ImmutableArray<string> ResourcesTrailingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(ResourcesTrailingComments));
    } = [];
}

/// <summary>A scene namespace: an XML prefix bound to a project folder (<c>xmlns:c="res://ui/"</c>).</summary>
/// <param name="Prefix">The XML prefix.</param>
/// <param name="Folder">The <c>res://</c> folder it names.</param>
public sealed record XscnNamespace(string Prefix, string Folder);

/// <summary>One entry of the <c>&lt;x:Uids&gt;</c> table: a file the document refers to and its uid.</summary>
/// <param name="Path">The <c>res://</c> path.</param>
/// <param name="Uid">The <c>uid://</c> text.</param>
public sealed record XscnUid(string Path, string Uid)
{
    /// <summary>The text of each comment just before the entry, in source order.</summary>
    public ImmutableArray<string> LeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(LeadingComments));
    } = [];
}

/// <summary>What an element's tag names.</summary>
public abstract record XscnType
{
    private protected XscnType() { }
}

/// <summary>An engine class the metadata knows.</summary>
/// <param name="Class">The class.</param>
public sealed record XscnClassType(GodotClassInfo Class) : XscnType;

/// <summary>A scene, instanced by a node or inherited by a root.</summary>
/// <param name="Path">The scene's <c>res://</c> path.</param>
public sealed record XscnSceneType(string Path) : XscnType;

/// <summary>A class the metadata does not know (a GDExtension class), kept by its tag name.</summary>
/// <param name="Name">The tag's name.</param>
public sealed record XscnUnknownType(string Name) : XscnType;

/// <summary>An element of the document, with the comments around it and where its source lies.</summary>
public abstract record XscnElement
{
    private protected XscnElement() { }

    /// <summary>The text of each comment just before the element, in source order.</summary>
    public ImmutableArray<string> LeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(LeadingComments));
    } = [];

    /// <summary>The text of each comment after the element's last child and before its end tag, in source order.</summary>
    public ImmutableArray<string> TrailingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(TrailingComments));
    } = [];

    /// <summary>The element's source, from its start tag's <c>&lt;</c> to the end of its end tag.</summary>
    public TextSpan Span { get; init; }
}

/// <summary>A node or an override: properties, connections, children and the <c>x:</c> attributes a node carries.</summary>
public abstract record XscnNodeElement : XscnElement
{
    private protected XscnNodeElement() { }

    /// <summary>The properties set on the element, in source order.</summary>
    public ImmutableArray<XscnProperty> Properties
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Properties));
    } = [];

    /// <summary>The connections the element emits, in call order.</summary>
    public ImmutableArray<XscnConnection> Connections
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Connections));
    } = [];

    /// <summary>The child nodes and overrides, in scene order.</summary>
    public ImmutableArray<XscnNodeElement> Children
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Children));
    } = [];

    /// <summary><c>x:Index</c>, the <c>.tscn</c>'s <c>index</c>, or null.</summary>
    public int? Index { get; init; }

    /// <summary><c>x:Owner</c>, the <c>.tscn</c>'s path of a non-root owner, or null.</summary>
    public string? Owner { get; init; }

    /// <summary><c>x:Editable</c>: whether the instance's children are editable (<c>[editable path=…]</c>).</summary>
    public bool Editable { get; init; }

    /// <summary><c>x:Groups</c>, the groups the node is in.</summary>
    public ImmutableArray<string> Groups
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Groups));
    } = [];

    /// <summary><c>x:InstancePlaceholder</c>, the scene path of an instance placeholder, or null.</summary>
    public string? InstancePlaceholder { get; init; }

    /// <summary><c>x:NodePaths</c>, the names of the properties that hold node paths.</summary>
    public ImmutableArray<string> NodePaths
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(NodePaths));
    } = [];

    /// <summary><c>x:ParentIdPath</c>, the parent's id path; empty when the <c>.tscn</c> has none.</summary>
    public ImmutableArray<int> ParentIdPath
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(ParentIdPath));
    } = [];

    /// <summary><c>x:OwnerUidPath</c>, the owner's id path; empty when the <c>.tscn</c> has none.</summary>
    public ImmutableArray<int> OwnerUidPath
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(OwnerUidPath));
    } = [];

    /// <summary><c>x:UniqueId</c>, the node's unique id, or null.</summary>
    public int? UniqueId { get; init; }
}

/// <summary>A node: an element whose tag is a class or a scene.</summary>
public sealed record XscnNode : XscnNodeElement
{
    /// <summary>What the tag names.</summary>
    public required XscnType Type { get; init; }

    /// <summary>The node's name, without the <c>%</c> of a unique name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the name is unique in its owner (<c>Name="%Press"</c>).</summary>
    public bool Unique { get; init; }
}

/// <summary>An <c>&lt;x:Override&gt;</c>: changes to a node inside an instanced scene.</summary>
public sealed record XscnOverride : XscnNodeElement
{
    /// <summary>The changed node's path, relative to the instance.</summary>
    public required string Path { get; init; }
}

/// <summary>A resource: an entry of <c>&lt;x:Resources&gt;</c>, an inline resource, or a <c>.xres</c> root.</summary>
public sealed record XscnResource : XscnElement
{
    /// <summary>The resource's class.</summary>
    public required XscnType Type { get; init; }

    /// <summary>The <c>x:Key</c> of a table entry, or null.</summary>
    public string? Key { get; init; }

    /// <summary>The properties set on the resource, in source order.</summary>
    public ImmutableArray<XscnProperty> Properties
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Properties));
    } = [];
}

/// <summary>A property set on an element; <c>script</c> is one like any other.</summary>
/// <param name="GodotName">The property's Godot name (<c>custom_minimum_size</c>, <c>theme_override_constants/separation</c>).</param>
/// <param name="Value">The value.</param>
public sealed record XscnProperty(string GodotName, XscnValue Value)
{
    /// <summary>The text of each comment just before an <c>&lt;x:Prop&gt;</c> or a property element, in source order.</summary>
    public ImmutableArray<string> LeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(LeadingComments));
    } = [];

    /// <summary>The text of each comment after a property element's resource and before its end tag, in source order.</summary>
    public ImmutableArray<string> TrailingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(TrailingComments));
    } = [];
}

/// <summary>A property's value.</summary>
public abstract record XscnValue
{
    private protected XscnValue() { }
}

/// <summary>A value of Godot's value grammar; an explicit null is <see cref="GodotNull"/>.</summary>
/// <param name="Value">The value.</param>
public sealed record XscnScalar(GodotValue Value) : XscnValue;

/// <summary>A file, written as its path.</summary>
/// <param name="Path">The <c>res://</c> path.</param>
public sealed record XscnFile(string Path) : XscnValue;

/// <summary>A reference to an <c>&lt;x:Resources&gt;</c> entry, written <c>{Key}</c>.</summary>
/// <param name="Key">The entry's key.</param>
public sealed record XscnKeyRef(string Key) : XscnValue;

/// <summary>A resource written inline, as a property element.</summary>
/// <param name="Resource">The resource.</param>
public sealed record XscnInline(XscnResource Resource) : XscnValue;

/// <summary>Text that is not a value of its property's type, kept with what was expected.</summary>
/// <param name="Text">The text as written.</param>
/// <param name="Message">What is wrong and what was expected.</param>
public sealed record XscnInvalid(string Text, string Message) : XscnValue;

/// <summary>A signal connection from the element that holds it.</summary>
/// <param name="Signal">The signal's Godot name.</param>
/// <param name="To">The receiver's path, as the <c>.tscn</c> gives it.</param>
/// <param name="Method">The method, by the script's own name.</param>
/// <param name="Flags">The connect flags, persist included.</param>
/// <param name="Binds">The bound arguments.</param>
/// <param name="Unbinds">The number of arguments dropped.</param>
/// <param name="FromUidPath">The emitter's id path; empty when the <c>.tscn</c> has none.</param>
/// <param name="ToUidPath">The receiver's id path; empty when the <c>.tscn</c> has none.</param>
public sealed record XscnConnection(
    string Signal,
    string To,
    string Method,
    int Flags,
    ImmutableArray<GodotValue> Binds,
    int Unbinds,
    ImmutableArray<int> FromUidPath,
    ImmutableArray<int> ToUidPath
)
{
    /// <summary>The bound arguments.</summary>
    public ImmutableArray<GodotValue> Binds
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(Binds));
    } = GodotValueTables.Required(Binds, nameof(Binds));

    /// <summary>The emitter's id path; empty when the <c>.tscn</c> has none.</summary>
    public ImmutableArray<int> FromUidPath
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(FromUidPath));
    } = GodotValueTables.Required(FromUidPath, nameof(FromUidPath));

    /// <summary>The receiver's id path; empty when the <c>.tscn</c> has none.</summary>
    public ImmutableArray<int> ToUidPath
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(ToUidPath));
    } = GodotValueTables.Required(ToUidPath, nameof(ToUidPath));

    /// <summary>The text of each comment just before an <c>&lt;x:Connect&gt;</c>, in source order.</summary>
    public ImmutableArray<string> LeadingComments
    {
        get;
        init => field = GodotValueTables.Required(value, nameof(LeadingComments));
    } = [];
}
