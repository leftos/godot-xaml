namespace GodotXaml.LanguageServer;

/// <summary>
/// Finds where a start tag ends by its '&lt;' and '&gt;' alone: a quoted run inside the tag is skipped and a '&lt;' ends a
/// tag left unclosed. Completion uses it for the attributes a tag already has.
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

    private static char Quote(char quote, char c) =>
        quote != '\0' ? (c == quote ? '\0' : quote)
        : c is '"' or '\'' ? c
        : '\0';
}
