using System.Collections.Immutable;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>Whether a document is a scene (<c>gd_scene</c>, .tscn) or a resource (<c>gd_resource</c>, .tres).</summary>
public enum TscnKind
{
    /// <summary>A <c>[gd_scene]</c> file: nodes, connections and editable paths, no <c>[resource]</c>.</summary>
    Scene,

    /// <summary>A <c>[gd_resource]</c> file: a <c>[resource]</c> section, no nodes.</summary>
    Resource,
}

/// <summary>A <c>name = value</c> line of a section, in source order.</summary>
/// <param name="Name">The property name, unquoted.</param>
/// <param name="Value">The value.</param>
public sealed record TscnProperty(string Name, GodotValue Value);

/// <summary>A section header attribute the model has no field for, kept as its source text.</summary>
/// <param name="Name">The attribute name.</param>
/// <param name="Text">The value's text as it stood in the source, trimmed.</param>
public sealed record TscnField(string Name, string Text);

/// <summary>The <c>[gd_scene]</c> or <c>[gd_resource]</c> header.</summary>
public sealed record TscnHeader
{
    /// <summary>The main resource's class (<c>type=</c>); resources only.</summary>
    public string? Type { get; init; }

    /// <summary>The global class name of the main resource's script (<c>script_class=</c>); resources only.</summary>
    public string? ScriptClass { get; init; }

    /// <summary>The file's <c>uid://</c> text, or null.</summary>
    public string? Uid { get; init; }

    /// <summary>The <c>format=</c> the file was read with; the writer recomputes it and does not read this.</summary>
    public int? Format { get; init; }

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>An <c>[ext_resource]</c>: a resource in another file.</summary>
public sealed record TscnExtResource
{
    /// <summary>The resource's class.</summary>
    public required string Type { get; init; }

    /// <summary>The resource's <c>uid://</c> text, or null.</summary>
    public string? Uid { get; init; }

    /// <summary>The resource's path.</summary>
    public required string Path { get; init; }

    /// <summary>The id <c>ExtResource("id")</c> refers to.</summary>
    public required string Id { get; init; }

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>A <c>[sub_resource]</c>: a resource built into the file.</summary>
public sealed record TscnSubResource
{
    /// <summary>The resource's class.</summary>
    public required string Type { get; init; }

    /// <summary>The id <c>SubResource("id")</c> refers to.</summary>
    public required string Id { get; init; }

    /// <summary>The properties, in source order.</summary>
    public ImmutableArray<TscnProperty> Properties { get; init; } = [];

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>A <c>[node]</c> of a scene, with every header attribute the 4.7.2 saver writes.</summary>
public sealed record TscnNode
{
    /// <summary>The node's name.</summary>
    public required string Name { get; init; }

    /// <summary>The node's class, or null for an instanced or inherited node.</summary>
    public string? Type { get; init; }

    /// <summary>The parent's path relative to the root (<c>.</c> for a child of the root), or null for the root.</summary>
    public string? Parent { get; init; }

    /// <summary>The owner's path, or null when the owner is the root.</summary>
    public string? Owner { get; init; }

    /// <summary>The child index, written for nodes of an inherited or instanced scene; null when not written.</summary>
    public int? Index { get; init; }

    /// <summary>The node's scene-unique id; null when unassigned.</summary>
    public int? UniqueId { get; init; }

    /// <summary>The names of the properties holding deferred NodePaths (<c>node_paths=</c>); empty when none.</summary>
    public ImmutableArray<string> NodePaths { get; init; } = [];

    /// <summary>The node's persistent groups; the writer sorts them as the saver does.</summary>
    public ImmutableArray<string> Groups { get; init; } = [];

    /// <summary>The path of the scene a placeholder loads (<c>instance_placeholder=</c>), or null.</summary>
    public string? InstancePlaceholder { get; init; }

    /// <summary>The instanced scene (<c>instance=</c>), usually an <c>ExtResource</c>, or null.</summary>
    public GodotValue? Instance { get; init; }

    /// <summary>The parent's id path (<c>parent_id_path=</c>); empty when not written.</summary>
    public ImmutableArray<int> ParentIdPath { get; init; } = [];

    /// <summary>The owner's id path (<c>owner_uid_path=</c>); empty when not written.</summary>
    public ImmutableArray<int> OwnerUidPath { get; init; } = [];

    /// <summary>The properties, in source order.</summary>
    public ImmutableArray<TscnProperty> Properties { get; init; } = [];

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>A <c>[connection]</c> of a scene.</summary>
public sealed record TscnConnection
{
    /// <summary>Godot's CONNECT_PERSIST, the flags the saver leaves unwritten.</summary>
    public const int PersistFlags = 2;

    /// <summary>The signal's name.</summary>
    public required string Signal { get; init; }

    /// <summary>The emitter's path relative to the root.</summary>
    public required string From { get; init; }

    /// <summary>The receiver's path relative to the root.</summary>
    public required string To { get; init; }

    /// <summary>The method called.</summary>
    public required string Method { get; init; }

    /// <summary>The connect flags; <see cref="PersistFlags"/> when the tag has none.</summary>
    public int Flags { get; init; } = PersistFlags;

    /// <summary>How many signal arguments are dropped; 0 when not written.</summary>
    public int Unbinds { get; init; }

    /// <summary>The bound arguments; empty when not written.</summary>
    public ImmutableArray<GodotValue> Binds { get; init; } = [];

    /// <summary>The emitter's id path (<c>from_uid_path=</c>); empty when not written.</summary>
    public ImmutableArray<int> FromUidPath { get; init; } = [];

    /// <summary>The receiver's id path (<c>to_uid_path=</c>); empty when not written.</summary>
    public ImmutableArray<int> ToUidPath { get; init; } = [];

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>An <c>[editable]</c> path: an instanced node whose children are editable.</summary>
public sealed record TscnEditable
{
    /// <summary>The instanced node's path relative to the root.</summary>
    public required string Path { get; init; }

    /// <summary>Attributes the model has no field for, in source order.</summary>
    public ImmutableArray<TscnField> Extra { get; init; } = [];
}

/// <summary>
/// A .tscn or .tres file as Godot 4.7.2's text saver writes it (scene/resources/resource_format_text.cpp,
/// ResourceFormatSaverTextInstance::save): the header and the sections, each in source order.
/// </summary>
public sealed record TscnDocument
{
    /// <summary>Scene or resource.</summary>
    public required TscnKind Kind { get; init; }

    /// <summary>The header.</summary>
    public TscnHeader Header { get; init; } = new();

    /// <summary>The <c>[ext_resource]</c> sections, in order.</summary>
    public ImmutableArray<TscnExtResource> ExtResources { get; init; } = [];

    /// <summary>The <c>[sub_resource]</c> sections, in order; a sub-resource comes after those it uses.</summary>
    public ImmutableArray<TscnSubResource> SubResources { get; init; } = [];

    /// <summary>The <c>[resource]</c> section's properties; resources only.</summary>
    public ImmutableArray<TscnProperty> ResourceProperties { get; init; } = [];

    /// <summary>The <c>[node]</c> sections, in order; scenes only.</summary>
    public ImmutableArray<TscnNode> Nodes { get; init; } = [];

    /// <summary>The <c>[connection]</c> sections, in order; scenes only.</summary>
    public ImmutableArray<TscnConnection> Connections { get; init; } = [];

    /// <summary>The <c>[editable]</c> sections, in order; scenes only.</summary>
    public ImmutableArray<TscnEditable> Editables { get; init; } = [];

    /// <summary>
    /// A new ext-resource id, <c>&lt;n&gt;_&lt;5 characters&gt;</c> with n one past the current count, as the editor's
    /// saver forms them (resource_format_text.cpp:1669); the characters are drawn from Godot's 34, <c>'a'</c> to
    /// <c>'y'</c> and <c>'0'</c> to <c>'8'</c> (core/io/resource.cpp:151-163), so an id never holds <c>'z'</c> or
    /// <c>'9'</c>. Unique among the ext ids and the same for the same document and seed.
    /// </summary>
    /// <param name="seed">The seed the five characters are drawn from.</param>
    /// <returns>The id.</returns>
    public string NewExtResourceId(ulong seed) =>
        TscnIdMinter.Mint(
            (ExtResources.Length + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            seed,
            ExtResources.Select(ext => ext.Id)
        );

    /// <summary>
    /// A new sub-resource id, <c>&lt;Type&gt;_&lt;5 characters&gt;</c> from the same 34-character set as
    /// <see cref="NewExtResourceId"/> (resource_format_text.cpp:1927); unique among the sub-resource ids and the same
    /// for the same document, type and seed.
    /// </summary>
    /// <param name="type">The sub-resource's class.</param>
    /// <param name="seed">The seed the five characters are drawn from.</param>
    /// <returns>The id.</returns>
    public string NewSubResourceId(string type, ulong seed)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        return TscnIdMinter.Mint(type, seed, SubResources.Select(sub => sub.Id));
    }
}
