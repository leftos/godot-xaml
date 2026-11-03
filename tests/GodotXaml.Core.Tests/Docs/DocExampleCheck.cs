using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Docs;

/// <summary>Checks one documented example against the parser and the writer.</summary>
internal static class DocExampleCheck
{
    /// <summary>Why a block does not hold, or null when it does.</summary>
    /// <param name="block">The block to check.</param>
    /// <param name="metadata">The metadata the parser and the writer use.</param>
    /// <returns>A failure message naming the block's path and line, or null.</returns>
    public static string? Check(DocBlock block, GodotMetadata metadata)
    {
        string where = $"{block.Path}:{block.Line}";
        XscnParseResult result = XscnParser.Parse(block.Text, block.Kind, metadata);

        if (block.InvalidCode is not null)
        {
            return result.Diagnostics.Any(diagnostic => diagnostic.Code == block.InvalidCode)
                ? null
                : $"{where}: the block must produce {block.InvalidCode}, but it produced {Codes(result)}.";
        }

        if (result.HasErrors)
        {
            return $"{where}: the block does not parse:{Problems(result)}";
        }

        if (!block.Canonical)
        {
            return null;
        }

        string written = XscnWriter.Write(result.Document, metadata);
        return written == block.Text ? null : $"{where}: the block is not canonical.\nDocumented:\n{block.Text}\nWritten:\n{written}";
    }

    private static string Codes(XscnParseResult result) =>
        result.Diagnostics.IsDefaultOrEmpty
            ? "no diagnostics"
            : string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal));

    private static string Problems(XscnParseResult result) =>
        string.Concat(
            result
                .Diagnostics.Where(diagnostic => diagnostic.Severity == XscnSeverity.Error)
                .Select(diagnostic => $"\n  {diagnostic.Code}: {diagnostic.Message}")
        );
}
