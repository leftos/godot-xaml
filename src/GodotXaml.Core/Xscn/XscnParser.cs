using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Core.Xscn;

/// <summary>What <see cref="XscnParser.Parse"/> gives: always a document, and every problem found on the way.</summary>
/// <param name="Document">The document, with whatever could be read of a broken file.</param>
/// <param name="Diagnostics">The problems, in source order.</param>
/// <param name="HasErrors">Whether any problem is an error, which fails a Godot load.</param>
public sealed record XscnParseResult(XscnDocument Document, ImmutableArray<XscnDiagnostic> Diagnostics, bool HasErrors);

/// <summary>Reads <c>.xscn</c> and <c>.xres</c> text into the model, tolerating broken text.</summary>
public static class XscnParser
{
    /// <summary>The namespace URI of the format's own elements and attributes, the <c>x:</c> prefix; it names the format's version.</summary>
    public const string FormatNamespace = "https://github.com/leftos/godot-xaml/format/1";

    /// <summary>Parses a document. Never throws on bad text: every problem becomes a diagnostic.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="kind">Whether the file is a scene or a resource.</param>
    /// <param name="metadata">The metadata of the Godot version the file is read for.</param>
    /// <returns>The document and the diagnostics.</returns>
    public static XscnParseResult Parse(string text, XscnDocumentKind kind, GodotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(metadata);
        var reader = new XscnTreeReader(text, metadata, NamesOf(metadata));
        XscnDocument document = reader.Read(kind);
        ImmutableArray<XscnDiagnostic> diagnostics = reader.Diagnostics;
        return new XscnParseResult(document, diagnostics, diagnostics.Any(d => d.Severity == XscnSeverity.Error));
    }

    // Indexing every class's properties is costly, so each metadata's names are built once.
    private static readonly ConditionalWeakTable<GodotMetadata, XscnPropertyNames> Names = new();

    /// <summary>The attribute names of a metadata, built once per metadata and shared with the writer.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <returns>Its attribute names.</returns>
    internal static XscnPropertyNames NamesOf(GodotMetadata metadata) => Names.GetValue(metadata, m => new XscnPropertyNames(m));
}
