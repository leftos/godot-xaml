using GodotXaml.Core.Diagnostics;
using Microsoft.Language.Xml;
using TextSpan = GodotXaml.Core.Diagnostics.TextSpan;

namespace GodotXaml.Core.Xscn;

/// <summary>
/// The XML parser's own errors (a missing <c>&gt;</c>, an unclosed tag), and the repeated attributes it does not report,
/// as diagnostics.
/// </summary>
internal static class XmlSyntaxErrors
{
    /// <summary>The parser's errors, then one error per repeated attribute.</summary>
    /// <param name="document">The parsed document.</param>
    /// <returns>The diagnostics.</returns>
    public static List<XscnDiagnostic> Collect(XmlDocumentSyntax document) => [.. ParserErrors(document), .. DuplicateAttributes(document)];

    // XML 1.0's "Unique Att Spec" constraint: an attribute name appears at most once in a tag. The error spans each later
    // occurrence's name.
    private static IEnumerable<XscnDiagnostic> DuplicateAttributes(XmlDocumentSyntax document) =>
        document
            .DescendantNodesAndTokensAndSelf(descendIntoChildren: null, descendIntoTrivia: false)
            .Where(n => n is XmlElementSyntax or XmlEmptyElementSyntax)
            .Cast<IXmlElementSyntax>()
            .SelectMany(Duplicates);

    private static IEnumerable<XscnDiagnostic> Duplicates(IXmlElementSyntax element)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (XmlAttributeSyntax attribute in element.Attributes)
        {
            if (attribute.NameNode is { FullName.Length: > 0 } name && !seen.Add(name.FullName))
            {
                string message = $"The attribute {name.FullName} is already set on this element; the first value is kept.";
                yield return new XscnDiagnostic(XscnCodes.DuplicateAttribute, XscnSeverity.Error, message, new TextSpan(name.SpanStart, name.Width));
            }
        }
    }

    // One diagnostic per offset where the tree holds an error. The parser marks an error on a token it found or on a token
    // it made up because the text lacks it; a made-up token has no text, so its error is placed where the text before it
    // ends rather than after the line break that follows.
    private static List<XscnDiagnostic> ParserErrors(XmlDocumentSyntax document)
    {
        List<XscnDiagnostic> found = [];
        HashSet<int> offsets = [];
        int textEnd = 0;
        foreach (SyntaxNode node in document.DescendantNodesAndTokensAndSelf(descendIntoChildren: null, descendIntoTrivia: true))
        {
            int offset = node.Width > 0 ? node.SpanStart : textEnd;
            if (node.GetDiagnostics() is { Length: > 0 } errors && offsets.Add(offset))
            {
                found.Add(new XscnDiagnostic(XscnCodes.XmlSyntax, XscnSeverity.Error, errors[0].GetDescription(), new TextSpan(offset, node.Width)));
            }

            if (node.IsToken && node.Width > 0)
            {
                textEnd = node.SpanStart + node.Width;
            }
        }

        return found;
    }
}
