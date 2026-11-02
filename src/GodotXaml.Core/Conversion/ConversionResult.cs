using System.Collections.Immutable;

namespace GodotXaml.Core.Conversion;

/// <summary>A converted document and the issues met on the way.</summary>
/// <typeparam name="T">The target document's type.</typeparam>
/// <param name="Document">The converted document; with an error issue it is not the whole source.</param>
/// <param name="Issues">The issues, in the order they were found.</param>
public sealed record ConversionResult<T>(T Document, ImmutableArray<ConversionIssue> Issues)
{
    /// <summary>
    /// The ids of the sections: from <see cref="TscnToXscn"/> the ones the source had, for
    /// <see cref="ConversionOptions.KeepIds"/>; from <see cref="XscnToTscn"/> the ones the converted document got.
    /// </summary>
    public ConversionIds? Ids { get; init; }
}
