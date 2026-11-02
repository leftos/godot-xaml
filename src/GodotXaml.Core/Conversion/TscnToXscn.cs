using System.Collections.Immutable;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Converts a .tscn or .tres model to an .xscn or .xres model. An ext_resource becomes its path where it is used, a
/// sub_resource used once as a bare property value goes inline and any other one into <c>x:Resources</c>; ids, the
/// header's <c>format</c>, <c>script_class</c> and <c>uid</c> are not kept (DECISIONS.md, "Where the rest of a .tscn
/// goes"). What the .xscn cannot hold is reported as an issue, never thrown.
/// </summary>
public static partial class TscnToXscn
{
    /// <summary>Converts a document.</summary>
    /// <param name="document">The .tscn or .tres model.</param>
    /// <param name="metadata">The engine classes, for the element types.</param>
    /// <param name="options">The options; the forward conversion reads none of them.</param>
    /// <returns>The .xscn or .xres model, its issues, and the source's ids for <see cref="ConversionOptions.KeepIds"/>.</returns>
    public static ConversionResult<XscnDocument> Convert(TscnDocument document, GodotMetadata metadata, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        if (document.Kind != TscnKind.Resource)
        {
            throw new NotSupportedException("Converting a scene (.tscn) to .xscn is not supported.");
        }

        var context = new Context(document, metadata);
        XscnDocument converted = context.ConvertResource();
        return new ConversionResult<XscnDocument>(converted, [.. context.Issues]) { Ids = context.Ids() };
    }

    private sealed partial class Context
    {
        private readonly TscnDocument document;
        private readonly GodotMetadata metadata;
        private readonly TscnReferences references;
        private readonly Dictionary<XscnResource, string> subIds = new(ReferenceEqualityComparer.Instance);

        public Context(TscnDocument document, GodotMetadata metadata)
        {
            this.document = document;
            this.metadata = metadata;
            ReportExtra(document);
            references = TscnReferences.Analyze(document, Issues);
        }

        public List<ConversionIssue> Issues { get; } = [];

        // The first ext_resource at a path gives the path's id, as the .xscn keeps one file per path.
        public ConversionIds Ids()
        {
            Dictionary<string, string> extIds = new(StringComparer.Ordinal);
            foreach (TscnExtResource ext in references.UsedExtResources)
            {
                extIds.TryAdd(ext.Path, ext.Id);
            }

            return new ConversionIds(extIds, subIds);
        }

        private ImmutableArray<XscnUid> Uids() =>
            [
                .. references
                    .UsedExtResources.Where(ext => ext.Uid is not null)
                    .DistinctBy(ext => ext.Path)
                    .Select(ext => new XscnUid(ext.Path, ext.Uid!)),
            ];

        private ImmutableArray<XscnResource> Table() => [.. references.KeyedSubResources.Select(sub => Resource(sub, references.KeyOf(sub.Id)))];

        private XscnResource Resource(TscnSubResource sub, string? key)
        {
            var resource = new XscnResource
            {
                Type = TypeOf(sub.Type, $"[sub_resource id=\"{sub.Id}\"]"),
                Key = key,
                Properties = Properties(sub.Properties),
            };
            subIds[resource] = sub.Id;
            return resource;
        }

        private ImmutableArray<XscnProperty> Properties(ImmutableArray<TscnProperty> properties) =>
            [.. properties.Select(property => new XscnProperty(property.Name, Value(property.Value)))];

        // A bare reference becomes a file, an inline resource or a key; any other value keeps its text, references
        // rewritten. A reference naming no section was reported by the analysis and is kept as it stands.
        private XscnValue Value(GodotValue value) =>
            value switch
            {
                GodotResourceRef { Kind: GodotRefKind.External } reference when references.Ext(reference.Id) is { } ext => FileValue(ext.Path),
                GodotResourceRef { Kind: GodotRefKind.Sub } reference when references.Sub(reference.Id) is { } sub => SubValue(sub),
                _ => new XscnScalar(ValueReferences.Map(value, Rewrite)),
            };

        // .xscn text reads a file only from a res:// path; any other path Godot accepts (a relative one,
        // resource_format_text.cpp:509-512) stays Resource("…") value text.
        private static XscnValue FileValue(string path) =>
            path.StartsWith("res://", StringComparison.Ordinal) ? new XscnFile(path) : new XscnScalar(new GodotPathRef(null, path));

        private XscnValue SubValue(TscnSubResource sub) =>
            references.IsInline(sub.Id) ? new XscnInline(Resource(sub, null)) : new XscnKeyRef(references.KeyOf(sub.Id));

        // Inside value text a file is Resource("res://…") and a sub-resource SubResource("Key") (xscn-core.md, References).
        private GodotValue Rewrite(GodotValue reference) =>
            reference switch
            {
                GodotResourceRef { Kind: GodotRefKind.External } r when references.Ext(r.Id) is { } ext => new GodotPathRef(null, ext.Path),
                GodotResourceRef { Kind: GodotRefKind.Sub } r when references.Sub(r.Id) is { } sub => new GodotResourceRef(
                    GodotRefKind.Sub,
                    references.KeyOf(sub.Id)
                ),
                _ => reference,
            };

        private XscnType TypeOf(string className, string location)
        {
            if (metadata.FindClass(className) is { } found)
            {
                return new XscnClassType(found);
            }

            Report(
                ConversionCodes.UnknownClass,
                ConversionSeverity.Warning,
                $"The metadata has no class '{className}'; it is kept by name.",
                location
            );
            return new XscnUnknownType(className);
        }

        private void ReportExtra(TscnDocument source)
        {
            ReportExtra(source.Header.Extra, source.Kind == TscnKind.Resource ? "[gd_resource]" : "[gd_scene]");
            foreach (TscnExtResource ext in source.ExtResources)
            {
                ReportExtra(ext.Extra, $"[ext_resource id=\"{ext.Id}\"]");
            }

            foreach (TscnSubResource sub in source.SubResources)
            {
                ReportExtra(sub.Extra, $"[sub_resource id=\"{sub.Id}\"]");
            }
        }

        private void ReportExtra(ImmutableArray<TscnField> extra, string location)
        {
            foreach (TscnField field in extra)
            {
                string message = $"The attribute {field.Name}={field.Text} has no place in .xscn; it is dropped.";
                Report(ConversionCodes.UnrepresentableAttribute, ConversionSeverity.Error, message, location);
            }
        }

        private void Report(string code, ConversionSeverity severity, string message, string location) =>
            Issues.Add(new ConversionIssue(code, severity, message, location));
    }
}
