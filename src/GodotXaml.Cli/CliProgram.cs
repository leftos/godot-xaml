using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace GodotXaml.Cli;

/// <summary>The parts of a run a caller can replace: the folder enumeration, the working directory and the metadata versions.</summary>
/// <param name="Enumerate">The files directly in a folder.</param>
/// <param name="WorkingDirectory">The folder the search for <c>godot-xaml.json</c> starts from.</param>
/// <param name="Versions">The Godot versions the run carries, and how it loads one's metadata.</param>
internal sealed record CliSeams(Func<string, IEnumerable<string>> Enumerate, string WorkingDirectory, MetadataVersions Versions)
{
    /// <summary>The real file system, the process's current working directory, and this build's bundled metadata.</summary>
    internal static CliSeams Default => new(folder => Directory.EnumerateFiles(folder), Environment.CurrentDirectory, MetadataVersions.Bundled);
}

/// <summary>The tool's command line: a command per way of using it, runnable in process so tests own the writers.</summary>
public static class CliProgram
{
    /// <summary>Runs the tool over the real file system.</summary>
    /// <param name="args">The command line, without the program's own name.</param>
    /// <param name="output">Where help and results go.</param>
    /// <param name="error">Where usage problems and failures go.</param>
    /// <returns>0 when no file has an error, 1 when one does or is refused, 2 for a usage or I/O error.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error) => Run(args, output, error, CliSeams.Default);

    /// <summary>Runs the tool, with the seams a caller can replace.</summary>
    /// <param name="args">The command line, without the program's own name.</param>
    /// <param name="output">Where help and results go.</param>
    /// <param name="error">Where usage problems and failures go.</param>
    /// <param name="seams">The folder enumeration, the working directory and the metadata versions the run uses.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error, CliSeams seams)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(seams);
        try
        {
            // Response files are off: a token starting with @ is a path like any other.
            ParseResult parseResult = Build(seams).Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
            if (parseResult.Errors.Count > 0)
            {
                foreach (ParseError parseError in parseResult.Errors)
                {
                    error.WriteLine(parseError.Message);
                }
                return 2;
            }
            if (RefusesOptionLikePath(parseResult, error))
            {
                return 2;
            }
            var configuration = new InvocationConfiguration
            {
                Output = output,
                Error = error,
                EnableDefaultExceptionHandler = false,
            };
            return parseResult.Invoke(configuration);
        }
        catch (Exception exception)
        {
            // The last line of defence, so a run inside a build script prints one line and never a stack trace.
            error.WriteLine($"godot-xaml: {exception.Message}");
            return 2;
        }
    }

    // A token that looks like an option is a usage error, as it would be had the parser not matched it to a path, until
    // the first `--`: after it every token is a path, for every command.
    private static bool RefusesOptionLikePath(ParseResult parseResult, TextWriter error)
    {
        if (
            parseResult.CommandResult.Command.Arguments.FirstOrDefault(argument => argument.Name == "path") is not Argument<string[]> paths
            || parseResult.CommandResult.GetResult(paths) is not { } result
        )
        {
            return false;
        }
        HashSet<Token> afterDoubleDash = TokensAfterDoubleDash(parseResult.Tokens);
        bool refused = false;
        foreach (Token token in result.Tokens)
        {
            if (token.Value.StartsWith('-') && !afterDoubleDash.Contains(token))
            {
                error.WriteLine($"Unrecognized command or argument '{token.Value}'.");
                refused = true;
            }
        }
        return refused;
    }

    // The parser's tokens keep the `--` itself, which no symbol result does, so the boundary is found here.
    private static HashSet<Token> TokensAfterDoubleDash(IReadOnlyList<Token> tokens)
    {
        var after = new HashSet<Token>(ReferenceEqualityComparer.Instance);
        bool past = false;
        foreach (Token token in tokens)
        {
            if (past)
            {
                after.Add(token);
            }
            else if (token.Type == TokenType.DoubleDash)
            {
                past = true;
            }
        }
        return after;
    }

    private static RootCommand Build(CliSeams seams)
    {
        var root = new RootCommand("Checks, converts and formats Godot scenes and resources written as XAML.");
        root.Subcommands.Add(CheckCommand.Create(seams.Enumerate, seams.Versions));
        root.Subcommands.Add(ConvertCommand.Create(seams.Enumerate, seams.WorkingDirectory, seams.Versions));
        root.Subcommands.Add(FormatCommand.Create(seams.Enumerate, seams.Versions));
        return root;
    }
}
