using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace GodotXaml.Cli;

/// <summary>The tool's command line: a command per way of using it, runnable in process so tests own the writers.</summary>
public static class CliProgram
{
    /// <summary>Runs the tool over the real file system.</summary>
    /// <param name="args">The command line, without the program's own name.</param>
    /// <param name="output">Where help and results go.</param>
    /// <param name="error">Where usage problems and failures go.</param>
    /// <returns>0 when no file has an error, 1 when one does, 2 when the command could not check anything.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error) => Run(args, output, error, folder => Directory.EnumerateFiles(folder));

    /// <summary>Runs the tool, with the folder enumeration a caller can replace to exercise a failing one.</summary>
    /// <param name="args">The command line, without the program's own name.</param>
    /// <param name="output">Where help and results go.</param>
    /// <param name="error">Where usage problems and failures go.</param>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error, Func<string, IEnumerable<string>> enumerate)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(enumerate);
        try
        {
            ParseResult parseResult = Build(enumerate).Parse(args);
            if (parseResult.Errors.Count > 0)
            {
                foreach (ParseError parseError in parseResult.Errors)
                {
                    error.WriteLine(parseError.Message);
                }
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

    private static RootCommand Build(Func<string, IEnumerable<string>> enumerate)
    {
        var root = new RootCommand("Checks Godot scenes and resources written as XAML.");
        root.Subcommands.Add(CheckCommand.Create(enumerate));
        return root;
    }
}
