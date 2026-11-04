namespace GodotXaml.LanguageServer;

/// <summary>
/// Finds where start tags end and which are open at an offset, by their '&lt;' and '&gt;' alone: a quoted run inside a tag
/// is skipped, a comment or a declaration is passed over, and a '&lt;' ends a tag left unclosed. Completion uses it for the
/// elements around a cursor, which the cursor context does not name.
/// </summary>
internal static class StartTags
{
    /// <summary>Where the tag that starts at a '&lt;' ends: its '&gt;', else the next '&lt;', else the text's end.</summary>
    /// <param name="text">The text.</param>
    /// <param name="lessThan">The tag's '&lt;'.</param>
    /// <returns>The offset of its '&gt;' or of the next '&lt;', or the text's length.</returns>
    public static int End(string text, int lessThan)
    {
        char quote = '\0';
        for (int i = lessThan + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<' || (quote == '\0' && c == '>'))
            {
                return i;
            }

            quote = Quote(quote, c);
        }

        return text.Length;
    }

    /// <summary>The '&lt;' of each start tag open at an offset, outermost first.</summary>
    /// <param name="text">The text.</param>
    /// <param name="offset">The offset; a tag that starts at or after it is not read.</param>
    /// <returns>The open tags' offsets.</returns>
    public static List<int> OpenAt(string text, int offset)
    {
        List<int> open = [];
        int next = text.IndexOf('<', StringComparison.Ordinal);
        while (next >= 0 && next < offset)
        {
            int after = Pass(text, next, open);
            next = after < text.Length ? text.IndexOf('<', after) : -1;
        }

        return open;
    }

    // Passes the markup at a '<', opening or closing an element; returns the offset after it.
    private static int Pass(string text, int lessThan, List<int> open)
    {
        if (string.CompareOrdinal(text, lessThan, "<!--", 0, 4) == 0)
        {
            int close = text.IndexOf("-->", lessThan + 4, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 3;
        }

        int end = End(text, lessThan);
        Track(text, lessThan, end, open);
        return end < text.Length && text[end] == '>' ? end + 1 : Math.Max(end, lessThan + 1);
    }

    // An end tag closes the innermost open element; a start tag not closed by '/>' opens one; '<?' and '<!' do neither.
    private static void Track(string text, int lessThan, int end, List<int> open)
    {
        char first = lessThan + 1 < text.Length ? text[lessThan + 1] : '\0';
        if (first == '/')
        {
            if (open.Count > 0)
            {
                open.RemoveAt(open.Count - 1);
            }
        }
        else if (first is not ('?' or '!') && !(end < text.Length && text[end] == '>' && text[end - 1] == '/'))
        {
            open.Add(lessThan);
        }
    }

    private static char Quote(char quote, char c) =>
        quote != '\0' ? (c == quote ? '\0' : quote)
        : c is '"' or '\'' ? c
        : '\0';
}
