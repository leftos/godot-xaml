using System.Collections.Immutable;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// A shader's local uniforms in the order ShaderMaterial lists its parameters, read from the shader's text without a
/// compiler. <c>ShaderLanguage::_parse_shader</c> (servers/rendering/shader_language.cpp) declares a uniform at global
/// scope as <c>[instance|global] uniform [precision] type name [: hints] [= default];</c>, with the array size after the type
/// or after the name, numbers each local one in declaration order (<c>prop_order</c>), gives it no number when it is an
/// <c>instance uniform</c> or a <c>global uniform</c>, and reads <c>group_uniforms A.B;</c> as group <c>A/B</c> and
/// <c>group_uniforms;</c> as closing the group. <c>ShaderMaterial::_get_property_list</c> (scene/resources/material.cpp)
/// then regroups them: groups in the order they first appear, the ungrouped ones where the first of them appeared, and
/// declaration order inside each group. <c>ShaderCompiler::_dump_node_code</c> (servers/rendering/shader_compiler.cpp)
/// leaves out a uniform hinted <c>hint_screen_texture</c>, <c>hint_normal_roughness_texture</c>, <c>hint_depth_texture</c>
/// or <c>hint_blit_source0</c> to <c>hint_blit_source3</c>, so the scan does too, and it opens no group. The scan skips
/// comments and string literals and reads only brace depth 0, where <c>uniform</c> is a reserved word, so each one there
/// starts a declaration. A preprocessor directive (<c>#include</c>, <c>#define</c>, <c>#if</c>) could add or hide a
/// uniform, so a <c>#</c> with only blanks or comments before it on its line gives no order, as
/// <c>ShaderPreprocessor::CommentRemover::strip</c> (servers/rendering/shader_preprocessor.cpp) removes comments first.
/// </summary>
internal static class ShaderUniforms
{
    private const string Ungrouped = "";

    private static readonly HashSet<string> Precisions = new(["lowp", "mediump", "highp"], StringComparer.Ordinal);

    // The hints whose uniform ShaderCompiler::_dump_node_code skips before adding it to the uniforms the material lists.
    private static readonly HashSet<string> EngineTextureHints = new(
        [
            "hint_screen_texture",
            "hint_normal_roughness_texture",
            "hint_depth_texture",
            "hint_blit_source0",
            "hint_blit_source1",
            "hint_blit_source2",
            "hint_blit_source3",
        ],
        StringComparer.Ordinal
    );

    /// <summary>The local uniforms of a shader's text, or null when a preprocessor directive could change them.</summary>
    /// <param name="code">The shader's text.</param>
    /// <returns>The names in Godot's order, or null.</returns>
    public static ImmutableArray<string>? Scan(string code) => Tokens(code) is { } tokens ? Regroup(Declarations(tokens)) : null;

    // Each local uniform with its group, in declaration order.
    private static List<(string Group, string Name)> Declarations(List<string> tokens)
    {
        List<(string Group, string Name)> uniforms = [];
        string group = Ungrouped;
        int index = 0;
        while (index < tokens.Count)
        {
            switch (tokens[index])
            {
                case "group_uniforms":
                    index = GroupOf(tokens, index + 1, out group);
                    break;
                case "instance" or "global" when Next(tokens, index + 1) == "uniform":
                    index = StatementEnd(tokens, index);
                    break;
                case "uniform":
                    int end = StatementEnd(tokens, index);
                    if (NameOf(tokens, index + 1) is { } name && !HasEngineTextureHint(tokens, index, end))
                    {
                        uniforms.Add((group, name));
                    }

                    index = end;
                    break;
                default:
                    index++;
                    break;
            }
        }

        return uniforms;
    }

    // group_uniforms A.B; names group A/B, group_uniforms; closes the group; the index past the statement.
    private static int GroupOf(List<string> tokens, int index, out string group)
    {
        List<string> parts = [];
        while (index < tokens.Count && tokens[index] != ";")
        {
            if (tokens[index] != ".")
            {
                parts.Add(tokens[index]);
            }

            index++;
        }

        group = string.Join('/', parts);
        return index + 1;
    }

    // The name after [precision] type, with an array size after the type skipped; null when the declaration is cut short.
    private static string? NameOf(List<string> tokens, int index)
    {
        if (Next(tokens, index) is { } precision && Precisions.Contains(precision))
        {
            index++;
        }

        index++;
        if (Next(tokens, index) == "[")
        {
            while (index < tokens.Count && tokens[index] != "]")
            {
                index++;
            }

            index++;
        }

        return Next(tokens, index) is { } name && IsIdentifier(name) ? name : null;
    }

    // The index past the next ; at depth 0, so a default value's parentheses and commas stay inside the statement.
    private static int StatementEnd(List<string> tokens, int index)
    {
        while (index < tokens.Count && tokens[index] != ";")
        {
            index++;
        }

        return index + 1;
    }

    // Whether a hint in the declaration names a texture the engine fills; each hint is a reserved word, so a match among the
    // statement's tokens can only be a hint.
    private static bool HasEngineTextureHint(List<string> tokens, int start, int end)
    {
        for (int index = start; index < end && index < tokens.Count; index++)
        {
            if (EngineTextureHints.Contains(tokens[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Next(List<string> tokens, int index) => index < tokens.Count ? tokens[index] : null;

    private static bool IsIdentifier(string token) => char.IsAsciiLetter(token[0]) || token[0] == '_';

    // Groups in the order they first appear, the ungrouped bucket among them where its first uniform appeared, and each
    // group's names in declaration order.
    private static ImmutableArray<string> Regroup(List<(string Group, string Name)> uniforms)
    {
        List<string> order = [];
        Dictionary<string, List<string>> groups = new(StringComparer.Ordinal);
        foreach ((string group, string name) in uniforms)
        {
            if (!groups.TryGetValue(group, out List<string>? names))
            {
                names = [];
                groups.Add(group, names);
                order.Add(group);
            }

            names.Add(name);
        }

        return [.. order.SelectMany(group => groups[group])];
    }

    // The depth-0 tokens of the text: identifiers, numbers and single punctuation marks, with comments and string literals
    // skipped and everything inside braces dropped (a brace itself included); null when a # has only blanks or comments
    // before it on its line, since the preprocessor strips comments first.
    private static List<string>? Tokens(string code)
    {
        List<string> tokens = [];
        int depth = 0;
        bool lineStart = true;
        int index = 0;
        while (index < code.Length)
        {
            char current = code[index];
            if (current == '\n')
            {
                lineStart = true;
                index++;
            }
            else if (char.IsWhiteSpace(current))
            {
                index++;
            }
            else if (current == '#' && lineStart)
            {
                return null;
            }
            else if (CommentEnd(code, index) is int end and >= 0)
            {
                // The preprocessor strips comments first, so a comment leaves the line's start open and a block
                // comment that spans lines puts the text after it at a line's start.
                lineStart |= code.AsSpan(index, end - index).Contains('\n');
                index = end;
            }
            else
            {
                lineStart = false;
                index = Token(code, index, ref depth, tokens);
            }
        }

        return tokens;
    }

    // Reads one token or string literal at the index, adding a token when at depth 0; the index past it.
    private static int Token(string code, int index, ref int depth, List<string> tokens)
    {
        switch (code[index])
        {
            case '"':
                return StringEnd(code, index + 1);
            case '{':
                depth++;
                return index + 1;
            case '}':
                depth = Math.Max(0, depth - 1);
                return index + 1;
            default:
                return Word(code, index, depth, tokens);
        }
    }

    // Reads an identifier, a number or one punctuation mark, adding it when at depth 0; the index past it.
    private static int Word(string code, int index, int depth, List<string> tokens)
    {
        int end = WordEnd(code, index);
        if (depth == 0)
        {
            tokens.Add(code[index..end]);
        }

        return end;
    }

    // The index past a comment at the index (a line comment ends before its newline), or -1 when none starts there.
    private static int CommentEnd(string code, int index)
    {
        char next = index + 1 < code.Length ? code[index + 1] : '\0';
        return (code[index], next) switch
        {
            ('/', '/') => EndOf(code, index, "\n", consume: false),
            ('/', '*') => EndOf(code, index + 2, "*/", consume: true),
            _ => -1,
        };
    }

    // The index past the end marker after the start (or at it, unconsumed), or the text's end when there is none.
    private static int EndOf(string code, int start, string marker, bool consume)
    {
        int found = code.IndexOf(marker, start, StringComparison.Ordinal);
        return found < 0 ? code.Length : found + (consume ? marker.Length : 0);
    }

    // The index past a string literal's closing quote, a backslash escaping the next character.
    private static int StringEnd(string code, int index)
    {
        while (index < code.Length && code[index] != '"')
        {
            index += code[index] == '\\' ? 2 : 1;
        }

        return Math.Min(index + 1, code.Length);
    }

    // The end of an identifier or a number, or of one punctuation mark.
    private static int WordEnd(string code, int index)
    {
        if (!IsWordPart(code[index]))
        {
            return index + 1;
        }

        int end = index + 1;
        while (end < code.Length && (IsWordPart(code[end]) || (char.IsAsciiDigit(code[index]) && code[end] == '.')))
        {
            end++;
        }

        return end;
    }

    private static bool IsWordPart(char unit) => char.IsAsciiLetterOrDigit(unit) || unit == '_';
}
