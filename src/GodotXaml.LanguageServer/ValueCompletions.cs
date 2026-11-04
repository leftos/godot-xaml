using System.Globalization;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>The candidates of a value and the text they replace.</summary>
/// <param name="Replace">The span each candidate replaces.</param>
/// <param name="Candidates">The candidates.</param>
internal sealed record ValueCandidates(TextSpan Replace, IEnumerable<CompletionCandidate> Candidates);

/// <summary>
/// The values a property attribute takes, by the property's type on the element's class: an enum's members over the whole
/// value; a flags member over the comma-separated part at the cursor, less the members the other parts hold; <c>True</c> and
/// <c>False</c> for a bool; and, for a resource property, a <c>{Key}</c> for each root <c>x:Resources</c> entry whose class
/// the property takes (an entry of unknown class included). An unknown class or attribute, an <c>x:</c> attribute, a signal
/// and every other type get none.
/// </summary>
/// <param name="scope">The document.</param>
/// <param name="context">The value's context.</param>
/// <param name="offset">The cursor's offset.</param>
internal sealed class ValueCompletions(CompletionScope scope, XscnCursorContext context, int offset)
{
    /// <summary>The candidates.</summary>
    /// <returns>The candidates and their range, or null where the value takes none.</returns>
    public ValueCandidates? Candidates()
    {
        if (Property() is not { } property)
        {
            return null;
        }

        XscnValueType type = XscnValueText.TypeOf(scope.Metadata, property);
        if ((type.Names ?? type.Declared?.Names) is { } names)
        {
            return Members(names);
        }

        if (type.VariantType == "bool")
        {
            return new ValueCandidates(Value(), [Bool("True"), Bool("False")]);
        }

        return property.Type == "Object" && NameRules.HoldsResource(scope.Metadata, property.ClassName)
            ? new ValueCandidates(Value(), Keys(property.ClassName))
            : null;
    }

    // The property of a class element's attribute, as hover resolves it; null for an x: attribute, a signal or an unknown name.
    private GodotPropertyInfo? Property()
    {
        if (
            context.AttributeName is not { } name
            || name.Contains(':', StringComparison.Ordinal)
            || scope.ShapeOf(context) != ElementShape.ClassElement
        )
        {
            return null;
        }

        return context.Class is { } cls ? scope.Metadata.FindPropertyByCSharpName(cls, name) : null;
    }

    private ValueCandidates? Members(XscnEnumNames names) =>
        names.Names.Length == 0 ? null
        : names.IsFlags ? Flags(names)
        : new ValueCandidates(Value(), names.Names.Select(Member));

    private static CompletionCandidate Member(XscnEnumName member) =>
        new(member.Name, CompletionItemKind.EnumMember, member.Value.ToString(CultureInfo.InvariantCulture));

    private static CompletionCandidate Bool(string label) => new(label, CompletionItemKind.Value, null);

    // The value's text on the cursor's line: an unterminated value runs on to the next quote, and an edit's range is one line.
    private TextSpan Value()
    {
        string text = scope.Text;
        int lineStart = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
        int lineEnd = text.IndexOfAny(['\r', '\n'], offset) is int newline and >= 0 ? newline : text.Length;
        int start = Math.Max(context.Replace.Start, lineStart);
        int end = Math.Min(context.Replace.End, lineEnd);
        return new TextSpan(start, end - start);
    }

    // The part at the cursor, trimmed of spaces, is replaced when the cursor is on it, else the cursor inserts; the names
    // the value holds outside the replaced text are not offered again.
    private ValueCandidates Flags(XscnEnumNames names)
    {
        TextSpan value = Value();
        string text = scope.Text;
        TextSpan part = PartAt(value);
        TextSpan replace = part.Length > 0 && part.Start <= offset && offset <= part.End ? part : new TextSpan(offset, 0);
        string outside = $"{text[value.Start..replace.Start]},{text[replace.End..value.End]}";
        HashSet<string> written = [.. outside.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        return new ValueCandidates(replace, names.Names.Where(n => !written.Contains(n.Name)).Select(Member));
    }

    // The comma-separated part of a value the cursor is in, trimmed of spaces.
    private TextSpan PartAt(TextSpan value)
    {
        string text = scope.Text;
        int start = offset > value.Start && text.LastIndexOf(',', offset - 1, offset - value.Start) is int comma and >= 0 ? comma + 1 : value.Start;
        int end = text.IndexOf(',', offset, value.End - offset) is int next and >= 0 ? next : value.End;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new TextSpan(start, end - start);
    }

    // The root's x:Resources entries whose class the hint takes, or whose class is unknown, as {Key} references; the entry
    // the cursor is in is left out, since an entry cannot refer to itself.
    private IEnumerable<CompletionCandidate> Keys(string hint)
    {
        XscnParseResult parsed = XscnParser.Parse(scope.Text, scope.Kind, scope.Metadata);
        foreach (XscnResource entry in parsed.Document.Resources.Where(r => r.Span.Start >= offset || offset >= r.Span.End))
        {
            string? detail = entry.Type switch
            {
                XscnClassType known when NameRules.Takes(scope.Metadata, hint, known.Class) => known.Class.CSharpName,
                XscnUnknownType unknown => unknown.Name,
                _ => null,
            };
            if (entry.Key is { } key && detail is not null)
            {
                yield return new CompletionCandidate("{" + key + "}", CompletionItemKind.Reference, detail);
            }
        }
    }
}
