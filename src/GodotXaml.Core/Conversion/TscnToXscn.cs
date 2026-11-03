using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Converts a .tscn or .tres model to an .xscn or .xres model, a scene's nodes nested as their parent paths say and its
/// connections on their emitters. An ext_resource becomes its path where it is used; a sub_resource used once as a bare
/// value of a property a property element can hold goes inline, and any other one into <c>x:Resources</c>. Ids, the
/// header's <c>format</c>, <c>script_class</c> and <c>uid</c> are not kept (DECISIONS.md, "Where the rest of a .tscn
/// goes"). What the .xscn cannot hold is reported as an issue, never thrown.
/// </summary>
public static partial class TscnToXscn
{
    /// <summary>Converts a document.</summary>
    /// <param name="document">The .tscn or .tres model.</param>
    /// <param name="metadata">The engine classes, for the element types.</param>
    /// <param name="options">
    /// The options; the forward conversion reads <see cref="ConversionOptions.Previous"/> and <see cref="ConversionOptions.Seed"/>.
    /// </param>
    /// <returns>The .xscn or .xres model, its issues, and the source's ids for <see cref="ConversionOptions.KeepIds"/>.</returns>
    public static ConversionResult<XscnDocument> Convert(TscnDocument document, GodotMetadata metadata, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        var context = new Context(document, metadata, options);
        XscnDocument converted = document.Kind == TscnKind.Scene ? context.ConvertScene() : context.ConvertResource();
        IReadOnlyDictionary<XscnResource, XscnResource> copies = new Dictionary<XscnResource, XscnResource>();
        if (options.Previous is { } previous)
        {
            (converted, copies) = PreviousComments.Carry(previous, converted, context.KeptPreviousKeys, context.Issues);
        }

        return new ConversionResult<XscnDocument>(converted, [.. context.Issues]) { Ids = context.Ids(copies) };
    }

    private sealed partial class Context
    {
        private readonly TscnDocument document;
        private readonly GodotMetadata metadata;
        private readonly TscnReferences references;
        private readonly Dictionary<XscnResource, string> subIds = new(ReferenceEqualityComparer.Instance);

        public Context(TscnDocument document, GodotMetadata metadata, ConversionOptions options)
        {
            this.document = document;
            this.metadata = metadata;
            ReportExtra(document);
            XscnPropertyNames names = XscnParser.NamesOf(metadata);
            IReadOnlyDictionary<string, XscnResource> previousById = options.Previous is { } previous
                ? PreviousKeys.EntriesById(previous, metadata, options.Seed)
                : new Dictionary<string, XscnResource>();
            references = TscnReferences.Analyze(
                document,
                Issues,
                (cls, name) => names.AttributeName(cls is null ? null : metadata.FindClass(cls), name) is not null,
                options.Previous,
                previousById
            );
        }

        public List<ConversionIssue> Issues { get; } = [];

        // The keys the previous document's x:Resources entries gave, which are the only ones that keep its comments.
        public IReadOnlySet<string> KeptPreviousKeys => references.KeptPreviousKeys;

        // The first ext_resource at a path gives the path's id, as the .xscn keeps one file per path. A sub_resource's id
        // goes to the copy the comment carry made of its resource, when it made one, so it names the returned document's.
        public ConversionIds Ids(IReadOnlyDictionary<XscnResource, XscnResource> copies)
        {
            Dictionary<string, string> extIds = new(StringComparer.Ordinal);
            foreach (TscnExtResource ext in references.UsedExtResources)
            {
                extIds.TryAdd(ext.Path, ext.Id);
            }

            Dictionary<XscnResource, string> returned = new(ReferenceEqualityComparer.Instance);
            foreach ((XscnResource resource, string id) in subIds)
            {
                returned[copies.GetValueOrDefault(resource) ?? resource] = id;
            }

            return new ConversionIds(extIds, returned);
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
                Properties = Properties(sub.Properties, $"[sub_resource id=\"{sub.Id}\"]"),
            };
            subIds[resource] = sub.Id;
            return resource;
        }

        private ImmutableArray<XscnProperty> Properties(ImmutableArray<TscnProperty> properties, string location) =>
            [.. properties.Select(property => Property(property, location)).OfType<XscnProperty>()];

        // A name holding a character XML 1.0 cannot hold has no attribute or x:Prop name to be written by: the property is
        // dropped, and its error keeps the result from being written.
        private XscnProperty? Property(TscnProperty property, string location)
        {
            if (XscnWriter.FirstForbidden(property.Name) < 0)
            {
                return new XscnProperty(property.Name, Value(property.Value));
            }

            string message = $"The property name \"{EscapedName(property.Name)}\" holds a character XML cannot hold; the property is dropped.";
            Report(ConversionCodes.PropertyNameNotXml, ConversionSeverity.Error, message, location);
            return null;
        }

        // The name with each character XML cannot hold written as a \uXXXX escape, the spelling Godot's string grammar
        // reads (variant_parser.cpp:315-316).
        private static string EscapedName(string name)
        {
            var text = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsSurrogatePair(name, i))
                {
                    text.Append(name, i, 2);
                    i++;
                }
                else if (XscnWriter.IsForbidden(name[i]))
                {
                    text.Append("\\u").Append(((int)name[i]).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    text.Append(name[i]);
                }
            }

            return text.ToString();
        }

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

        // Inside value text a file is Resource("res://…") and a sub-resource SubResource("Key")
        // (docs/DECISIONS.md, "The core's implementation choices", References).
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
