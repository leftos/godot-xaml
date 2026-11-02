namespace GodotXaml.Cli;

/// <summary>The tool's entry point.</summary>
public static class Program
{
    /// <summary>Runs the tool over the console.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The tool's exit code.</returns>
    public static int Main(string[] args) => CliProgram.Run(args, Console.Out, Console.Error);
}
