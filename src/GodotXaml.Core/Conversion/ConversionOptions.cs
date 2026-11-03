using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>What a conversion needs beyond the document and the metadata.</summary>
public sealed record ConversionOptions
{
    /// <summary>
    /// The seed new <c>[ext_resource]</c> and <c>[sub_resource]</c> ids are drawn from; the same seed gives the same ids. A
    /// forward conversion with <see cref="Previous"/> uses it to find the ids that load minted, so a host passes the seed
    /// it loaded with.
    /// </summary>
    public ulong Seed { get; init; }

    /// <summary>The host's lookup of an ext_resource's class, asked before the table by extension; null when the host has none.</summary>
    public IExtResourceTypes? ExtResourceTypes { get; init; }

    /// <summary>
    /// The global class name (<c>class_name</c>) of the script at a <c>res://</c> path, or null when it has none; a .tres
    /// header's <c>script_class=</c> comes from it. When this is null, a resource with a script gets no
    /// <c>script_class=</c> and a warning.
    /// </summary>
    public Func<string, string?>? ScriptClassOf { get; init; }

    /// <summary>The <c>uid://</c> text the written file's header carries (its <c>.uid</c> sidecar's), or null for none.</summary>
    public string? Uid { get; init; }

    /// <summary>
    /// The ids a forward conversion recorded (<see cref="ConversionResult{T}.Ids"/>), reused on the way back in place of
    /// new ones so the text can be compared byte for byte with the source; null mints new ids from <see cref="Seed"/>.
    /// </summary>
    public ConversionIds? KeepIds { get; init; }

    /// <summary>
    /// The <c>.xscn</c> or <c>.xres</c> a forward conversion replaces: its <c>x:Resources</c> entries pair with the
    /// sub-resources by the id <see cref="Seed"/> mints first, then by name or properties, and the keys of the pairs are
    /// kept; its comments are carried onto the parts of the new document that match it; null converts from scratch.
    /// </summary>
    public XscnDocument? Previous { get; init; }

    /// <summary>
    /// The sub_resources the file being saved owns, for a forward conversion with <see cref="Previous"/>: each one's id in
    /// the <c>.tscn</c> text to the id it had when the file being replaced loaded it. Only these pair with an entry of
    /// <see cref="Previous"/> by id: a sub-resource of another file can carry an entry's id, and Godot's text saver then
    /// re-mints the id of whichever of the two it meets second. The save route builds it from the live resources' paths
    /// (<c>&lt;file&gt;::&lt;id&gt;</c>); null pairs every sub_resource by its text id, as the CLI and a conversion of a file do.
    /// </summary>
    public IReadOnlyDictionary<string, string>? OwnedSubResourceIds { get; init; }

    /// <summary>
    /// The host's reader of the scenes the document instances, which gives the way back the class of an instanced node,
    /// an inherited root or an <c>x:Override</c>, so their properties come back in Godot's order; with this or
    /// <see cref="SourceKey"/> null, those sections keep source order.
    /// </summary>
    public ISceneFiles? SceneFiles { get; init; }

    /// <summary>The host's key for the file being converted, the first referrer <see cref="SceneFiles"/> is asked with.</summary>
    public string? SourceKey { get; init; }
}
