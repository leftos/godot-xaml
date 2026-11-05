using GodotXaml.Core.Metadata;
using Microsoft.Language.Xml;

namespace GodotXaml.Core.Xscn;

/// <summary>
/// Reads a document's <c>x:Godot</c> marker, the Godot version whose metadata wrote it, without the metadata a full parse
/// needs; so a host can learn which metadata to parse a file with before it parses it.
/// </summary>
public static class XscnMarker
{
    /// <summary>
    /// Reads the marker of a document's root. Works from the text alone: the marker is read with the same rules as
    /// <see cref="XscnParser.Parse"/>, and a marker the parse reports as malformed is read as none.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>
    /// The marker (<c>4.7</c>), or null when the text has no root element, the root binds no prefix to the format
    /// namespace, the root has no marker, or the marker is not a Godot version as <c>&lt;major&gt;.&lt;minor&gt;</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static string? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        XmlDocumentSyntax syntax = Parser.ParseText(text);
        return syntax.RootSyntax is { } root ? MarkerOf(root) : null;
    }

    /// <summary>
    /// The one warning every host prints when it reads a file whose marker names a Godot version it does not carry.
    /// </summary>
    /// <param name="marker">The version the file's marker names (<c>4.8</c>).</param>
    /// <param name="used">The version the tool reads the file with (<c>4.7</c>).</param>
    /// <returns>The warning, naming both versions.</returns>
    public static string NotCarried(string marker, string used) =>
        $"written with Godot {marker}'s metadata, which this tool does not carry; reading it with {used}'s";

    /// <summary>
    /// The one refusal every write path prints when it is asked to write a file whose marker names a Godot version the tool
    /// does not carry; writing would restamp the file with the version's metadata it has.
    /// </summary>
    /// <param name="marker">The version the file's marker names (<c>4.8</c>).</param>
    /// <param name="used">The version the tool carries (<c>4.7</c>).</param>
    /// <returns>The refusal, naming both versions and saying why the file is left as it is.</returns>
    public static string NotWritten(string marker, string used) =>
        $"written with Godot {marker}'s metadata, which this tool does not carry (it has {used}'s); "
        + $"not written, so the file keeps {marker}'s typing and marker";

    // A root with no name is a half-typed tag, which the parse reports; it has no marker.
    private static string? MarkerOf(IXmlElementSyntax root)
    {
        if (root.NameNode is not { FullName.Length: > 0 })
        {
            return null;
        }

        return FormatPrefix(root) is { } formatPrefix ? MarkerUnder(formatPrefix, root) : null;
    }

    private static string? MarkerUnder(string formatPrefix, IXmlElementSyntax root) =>
        FirstOfEachName(root).Find(attribute => IsMarker(attribute.NameNode, formatPrefix)) is { } marker ? WellFormed(marker.Value ?? "") : null;

    private static bool IsMarker(XmlNameSyntax? name, string prefix) =>
        name is { Prefix.Length: > 0 } && name.Prefix == prefix && name.LocalName == XscnFormatVocabulary.Godot;

    // A value the parse reports as a malformed marker (GX0121) is read as none.
    private static string? WellFormed(string raw)
    {
        string value = XmlText.DecodeAttribute(raw);
        return GodotMetadata.IsMinorVersion(value) ? value : null;
    }

    // The prefix bound to the format namespace by the root's first such xmlns attribute, as the reader binds it; a later
    // binding of the format namespace is ignored.
    private static string? FormatPrefix(IXmlElementSyntax root)
    {
        foreach (XmlAttributeSyntax attribute in FirstOfEachName(root))
        {
            if (attribute.NameNode is { Prefix: "xmlns" } name && XmlText.DecodeAttribute(attribute.Value ?? "") == XscnParser.FormatNamespace)
            {
                return name.LocalName;
            }
        }

        return null;
    }

    // A repeated attribute is reported with the XML errors; the first of each name is the one read, as the reader reads it.
    private static List<XmlAttributeSyntax> FirstOfEachName(IXmlElementSyntax element)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        return [.. element.Attributes.Where(a => a.NameNode is not { FullName.Length: > 0 } name || seen.Add(name.FullName))];
    }
}
