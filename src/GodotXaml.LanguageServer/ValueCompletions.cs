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
/// value; a flags member over the space- or comma-separated word at the cursor, less the members the other parts hold;
/// <c>True</c> and <c>False</c> for a bool; and, for a resource property, a <c>{Key}</c> for each root <c>x:Resources</c>
/// entry whose class the property takes (an entry of unknown class included). An unknown class or attribute, an <c>x:</c>
/// attribute, a signal and every other type get none.
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
            return Values([Bool("True"), Bool("False")]);
        }

        return property.Type == "Object" && NameRules.HoldsResource(scope.Metadata, property.ClassName) ? Values(Keys(property.ClassName)) : null;
    }

    // The candidates over the value, or none where the value starts on an earlier line.
    private ValueCandidates? Values(IEnumerable<CompletionCandidate> candidates) =>
        Value() is { } value ? new ValueCandidates(value, candidates) : null;

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

    private ValueCandidates? Members(XscnEnumNames names)
    {
        if (names.Names.Length == 0)
        {
            return null;
        }

        if (names.IsFlags)
        {
            return Flags(names);
        }

        return Values(names.Names.Select(m => Member(m, m.Name)));
    }

    private static CompletionCandidate Member(XscnEnumName member, string insert) =>
        new(member.Name, CompletionItemKind.EnumMember, member.Value.ToString(CultureInfo.InvariantCulture), insert);

    private static CompletionCandidate Bool(string label) => new(label, CompletionItemKind.Value, null, label);

    // The value's text on the cursor's line, since an edit's range is one line: a value that starts on an earlier line
    // gets none, since replacing its part on this line would leave that earlier text in place, and an unterminated value
    // runs on to the next quote with its range stopping at the cursor's line end.
    private TextSpan? Value()
    {
        string text = scope.Text;
        int lineStart = offset > 0 ? text.LastIndexOfAny(['\r', '\n'], offset - 1) + 1 : 0;
        if (context.Replace.Start < lineStart)
        {
            return null;
        }

        int lineEnd = text.IndexOfAny(['\r', '\n'], offset) is int newline and >= 0 ? newline : text.Length;
        int end = Math.Min(context.Replace.End, lineEnd);
        return new TextSpan(context.Replace.Start, end - context.Replace.Start);
    }

    // The word at the cursor is replaced when the cursor is in it or at its end, else the cursor inserts beside a part,
    // with the comma the value needs between them; the names the value holds outside the replaced text are not offered
    // again.
    private ValueCandidates? Flags(XscnEnumNames names)
    {
        if (Value() is not { } value)
        {
            return null;
        }

        TextSpan part = PartAt(value);
        if (part.Length > 0 && part.Start <= offset && offset <= part.End)
        {
            return new ValueCandidates(part, Unwritten(names, Written(value, part)).Select(m => Member(m, m.Name)));
        }

        string prefix = PartPrecedes(value) ? ", " : "";
        string suffix = SeparatorAfter(value);
        TextSpan replace = new(offset, 0);
        HashSet<string> written = Written(value, replace);
        return new ValueCandidates(replace, Unwritten(names, written).Select(n => Member(n, prefix + n.Name + suffix)));
    }

    // The names the value holds outside the replaced text, split on the commas and whitespace between them.
    private HashSet<string> Written(TextSpan value, TextSpan replace)
    {
        string text = scope.Text;
        string outside = $"{text[value.Start..replace.Start]},{text[replace.End..value.End]}";
        return [.. outside.Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(Words)];
    }

    // A comma-free run's words, split at every character the part test calls whitespace.
    private static string[] Words(string run) => run.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<XscnEnumName> Unwritten(XscnEnumNames names, HashSet<string> written) =>
        names.Names.Where(n => !written.Contains(n.Name));

    // Whether a part precedes the cursor in the value with a comma nowhere between them, since an insert after it takes
    // the comma the value needs.
    private bool PartPrecedes(TextSpan value)
    {
        if (offset <= value.Start)
        {
            return false;
        }

        string text = scope.Text;
        int comma = text.LastIndexOf(',', offset - 1, offset - value.Start);
        return text[Math.Max(comma + 1, value.Start)..offset].TrimEnd().Length > 0;
    }

    // The separator an insert at the cursor needs before a part that follows it in the value; empty when none does.
    private string SeparatorAfter(TextSpan value)
    {
        string text = scope.Text;
        if (offset >= value.End)
        {
            return "";
        }

        int comma = text.IndexOf(',', offset, value.End - offset);
        string after = text[offset..(comma >= 0 ? comma : value.End)];
        if (after.TrimStart().Length == 0)
        {
            return "";
        }

        return char.IsWhiteSpace(text[offset]) ? "," : ", ";
    }

    // The word of a value the cursor is in or at the end of: a run of characters that are neither a comma nor whitespace.
    // A cursor outside every word gets an empty span, which inserts instead of replacing.
    private TextSpan PartAt(TextSpan value)
    {
        string text = scope.Text;
        int at = Math.Clamp(offset, value.Start, value.End);

        // A cursor at a word's end belongs to that word, as a cursor on one of its characters does.
        if (!IsWordText(text, value, at) && at > value.Start && IsWordText(text, value, at - 1))
        {
            at--;
        }

        if (!IsWordText(text, value, at))
        {
            return new TextSpan(at, 0);
        }

        int start = WordStart(text, value, at);
        int end = WordEnd(text, value, at);
        return new TextSpan(start, end - start);
    }

    // The first index of the word holding `at`.
    private static int WordStart(string text, TextSpan value, int at)
    {
        int start = at;
        while (IsWordText(text, value, start - 1))
        {
            start--;
        }
        return start;
    }

    // The index after the word holding `at`.
    private static int WordEnd(string text, TextSpan value, int at)
    {
        int end = at + 1;
        while (IsWordText(text, value, end))
        {
            end++;
        }
        return end;
    }

    private static bool IsWordText(string text, TextSpan value, int index) =>
        index >= value.Start && index < value.End && text[index] != ',' && !char.IsWhiteSpace(text[index]);

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
                string label = "{" + key + "}";
                yield return new CompletionCandidate(label, CompletionItemKind.Reference, detail, Escaped(label));
            }
        }
    }

    // A key's text as the document writes it, since the key holds the characters an attribute escapes.
    private static string Escaped(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal)
            .Replace("\t", "&#9;", StringComparison.Ordinal)
            .Replace("\n", "&#10;", StringComparison.Ordinal)
            .Replace("\r", "&#13;", StringComparison.Ordinal);
}
