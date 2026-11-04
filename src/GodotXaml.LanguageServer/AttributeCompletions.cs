using System.Text.RegularExpressions;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The attribute names a start tag takes, less those it already has: on a class element of known class, its stored
/// properties, its signals when it is a node (a property of the same name wins), its theme overrides and the format's
/// attributes of its kind; on an instance, an override, a scene tag or an unknown class, the format's attributes alone.
/// A property element and an unknown prefix take none.
/// </summary>
/// <param name="scope">The document.</param>
/// <param name="context">The attribute name's or the start tag's context.</param>
internal sealed partial class AttributeCompletions(CompletionScope scope, XscnCursorContext context)
{
    /// <summary>The candidates.</summary>
    /// <returns>The candidates, or null where the tag takes none.</returns>
    public IEnumerable<CompletionCandidate>? Candidates()
    {
        int lessThan = scope.Text.LastIndexOf('<', Math.Max(context.Replace.Start - 1, 0));
        if (lessThan < 0)
        {
            return null;
        }

        IEnumerable<CompletionCandidate>? all = scope.ShapeOf(context) switch
        {
            ElementShape.ClassElement => ClassElement(lessThan),
            ElementShape.FormatElement when context.ElementName is { } name && XscnFormatVocabulary.Elements.Contains(name) => Format(name),
            ElementShape.SceneTag => Format(XscnFormatVocabulary.Node),
            _ => null,
        };
        if (all is null)
        {
            return null;
        }

        HashSet<string> written = Written(lessThan);
        return all.Where(c => !written.Contains(c.Label));
    }

    private IEnumerable<CompletionCandidate> Format(string elementKind) => scope.Keywords(XscnFormatVocabulary.AttributesFor(elementKind));

    // The format's attributes first, so a property or signal named like one (Script, Name) does not replace it.
    private IEnumerable<CompletionCandidate> ClassElement(int lessThan)
    {
        string kind = KindOf(lessThan);
        IEnumerable<CompletionCandidate> format = Format(kind);
        return context.Class is { } cls ? format.Concat(Members(cls, kind == XscnFormatVocabulary.Node)) : format;
    }

    // A node, a resource or an x:Resources entry: by the class, else by the elements it is in, else by the document's kind.
    private string KindOf(int lessThan)
    {
        if (context.Class is { } cls && scope.Metadata.Inherits(cls, "Node"))
        {
            return XscnFormatVocabulary.Node;
        }

        List<XscnCursorContext> chain = [.. scope.OpenElements(lessThan), context];
        if (chain.Count > 1 && chain[^2] is { } parent && scope.ShapeOf(parent) == ElementShape.FormatElement && parent.ElementName == "Resources")
        {
            return XscnFormatVocabulary.TableEntry;
        }

        return scope.IsResource(chain, chain.Count - 1) ? XscnFormatVocabulary.Resource : XscnFormatVocabulary.Node;
    }

    private IEnumerable<CompletionCandidate> Members(GodotClassInfo cls, bool isNode)
    {
        IEnumerable<CompletionCandidate> properties = scope
            .Metadata.PropertiesOf(cls)
            .Where(p => p.IsStored && !p.Name.Contains('/'))
            .Select(p => Spelled(cls, p.Name, scope.TypeName(p.Type, p.ClassName)))
            .OfType<CompletionCandidate>();
        IEnumerable<CompletionCandidate> themeOverrides = cls
            .ThemeOverrides.Select(t => Spelled(cls, t.Name, scope.TypeName(t.Type, t.HintString)))
            .OfType<CompletionCandidate>();
        return isNode ? properties.Concat(Signals(cls)).Concat(themeOverrides) : properties.Concat(themeOverrides);
    }

    // A property or theme override by the attribute name that reads back as it; null when it needs x:Prop.
    private CompletionCandidate? Spelled(GodotClassInfo cls, string godotName, string detail) =>
        scope.Names.AttributeName(cls, godotName) is { } name ? new CompletionCandidate(name, CompletionItemKind.Property, detail) : null;

    private IEnumerable<CompletionCandidate> Signals(GodotClassInfo cls) =>
        scope
            .Metadata.SignalsOf(cls)
            .Select(s => s.CSharpName!)
            .Where(name => scope.Metadata.FindPropertyByCSharpName(cls, name) is null)
            .Select(name => new CompletionCandidate(name, CompletionItemKind.Event, "signal"));

    // The names of the attributes the start tag already has, the one at the cursor left out.
    private HashSet<string> Written(int lessThan)
    {
        string tag = scope.Text[lessThan..StartTags.End(scope.Text, lessThan)];
        TextSpan replace = context.Replace;
        return [.. WrittenAttribute().Matches(tag).Where(m => lessThan + m.Groups[1].Index != replace.Start).Select(m => m.Groups[1].Value)];
    }

    // An attribute's name, its '=' and its quoted value, if it has one.
    [GeneratedRegex("""([^\s=<>"'/]+)\s*=\s*(?:"[^"]*"|'[^']*')?""", RegexOptions.CultureInvariant)]
    private static partial Regex WrittenAttribute();
}
