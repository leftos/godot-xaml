using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The <c>.xscn</c> or <c>.xres</c> a forward conversion replaces, and the metadata it was read with: its loader minted its
/// sub-resource ids with that metadata, so its entries are paired by those ids.
/// </summary>
/// <param name="Document">The document the conversion replaces.</param>
/// <param name="Metadata">The metadata the document was read with.</param>
public sealed record PreviousDocument(XscnDocument Document, GodotMetadata Metadata);
