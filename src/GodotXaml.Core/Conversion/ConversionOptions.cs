using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>What a conversion needs beyond the document and the metadata.</summary>
public sealed record ConversionOptions
{
    /// <summary>The seed new <c>[ext_resource]</c> and <c>[sub_resource]</c> ids are drawn from; the same seed gives the same ids.</summary>
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
    /// The <c>.xscn</c> or <c>.xres</c> a forward conversion replaces: its <c>x:Resources</c> keys are kept for the
    /// sub-resources that match its entries, and its comments are carried onto the parts of the new document that match
    /// it; null converts from scratch.
    /// </summary>
    public XscnDocument? Previous { get; init; }
}
