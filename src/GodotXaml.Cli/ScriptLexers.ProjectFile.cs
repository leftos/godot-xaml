namespace GodotXaml.Cli;

/// <summary>The <c>project.godot</c> lexer, after Godot's <c>VariantParser::get_token</c>.</summary>
internal static partial class ScriptLexers
{
    /// <summary>The string literals of a <c>project.godot</c> text. Only a double quote opens a string, and it may span lines.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The literals, in offset order.</returns>
    internal static List<ScriptLiteral> ProjectFile(string text)
    {
        List<ScriptLiteral> found = [];
        int at = 0;
        while (at < text.Length)
        {
            char current = text[at];
            if (current == ';')
            {
                at = GodotLineEnd(text, at);
            }
            else if (current == '"')
            {
                ScriptLiteral? literal = Escaped(text, at, 1, GodotEscape);
                if (literal is null)
                {
                    return found;
                }
                found.Add(literal);
                at = literal.End + 1;
            }
            else
            {
                at++;
            }
        }
        return found;
    }
}
