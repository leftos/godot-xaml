using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>The <c>check</c> command: parses every given file and prints each problem in MSBuild's form.</summary>
internal static class CheckCommand
{
    private static readonly AcceptedFiles<XscnDocumentKind> Accepted = new(InputFiles.XscnExtensions, "only .xscn and .xres files can be checked.");

    /// <summary>The command, its paths argument and its metadata option.</summary>
    /// <param name="enumerate">The files of a folder and its subfolders.</param>
    /// <returns>The command.</returns>
    internal static Command Create(Func<string, IEnumerable<string>> enumerate)
    {
        Argument<string[]> paths = PathsArgument("A .xscn or .xres file, or a folder searched recursively for both.");
        Option<string> metadata = MetadataOption();
        var command = new Command("check", "Parses .xscn and .xres files and reports every problem found.");
        command.Arguments.Add(paths);
        command.Options.Add(metadata);
        command.SetAction(parseResult => Run(parseResult, paths, metadata, enumerate));
        return command;
    }

    /// <summary>The paths argument every command takes: one or more, none of them starting with <c>-</c>.</summary>
    /// <param name="description">What a path may be, for the help.</param>
    /// <returns>The argument.</returns>
    internal static Argument<string[]> PathsArgument(string description)
    {
        Argument<string[]> paths = new("path") { Description = description, Arity = ArgumentArity.OneOrMore };
        // A token that looks like an option is a usage error, as it would be had the parser not matched it to a path.
        paths.Validators.Add(result =>
        {
            foreach (Token token in result.Tokens)
            {
                if (token.Value.StartsWith('-'))
                {
                    result.AddError($"Unrecognized command or argument '{token.Value}'.");
                }
            }
        });
        return paths;
    }

    /// <summary>The <c>--metadata</c> option every command takes.</summary>
    /// <returns>The option.</returns>
    internal static Option<string> MetadataOption()
    {
        Option<string> metadata = new("--metadata")
        {
            Description = "The Godot metadata file; default, the metadata/godot-4.7.json shipped beside the tool.",
            HelpName = "file",
        };
        // An empty or blank value would reach the loader as a path of its own; it is a missing argument, not a file.
        metadata.Validators.Add(result =>
        {
            if (result.Tokens.Count > 0 && string.IsNullOrWhiteSpace(result.Tokens[0].Value))
            {
                result.AddError("--metadata needs a file path.");
            }
        });
        return metadata;
    }

    /// <summary>One diagnostic as MSBuild prints it, so editors and CI read it without a prefix of their own.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="map">The file's line map.</param>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>The line.</returns>
    internal static string Format(string path, LineMap map, XscnDiagnostic diagnostic)
    {
        LinePosition position = map.OneBased(diagnostic.Span.Start);
        string severity = diagnostic.Severity == XscnSeverity.Error ? "error" : "warning";
        return $"{path}({position.Line},{position.Column}): {severity} {diagnostic.Code}: {diagnostic.Message}";
    }

    /// <summary>Whether an exception is the core refusing a document it cannot convert or write, rather than a bug.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>Whether it is a refusal the file's line reports.</returns>
    internal static bool IsCoreRefusal(Exception exception) =>
        exception is NotSupportedException or (ArgumentException and not ArgumentNullException);

    /// <summary>The line for a file the core refused.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="exception">The refusal.</param>
    /// <returns>The line.</returns>
    internal static string CoreRefusal(string path, Exception exception) => $"{path}: error {CliCodes.CoreRefusal}: {exception.Message}";

    // Exit 2 is everything that stops the check before a file is judged: a path that does not exist, a folder or file
    // that cannot be read, a file that is not UTF-8 text, or an unreadable metadata file.
    private static int Run(ParseResult parseResult, Argument<string[]> paths, Option<string> metadata, Func<string, IEnumerable<string>> enumerate)
    {
        InvocationConfiguration invocation = parseResult.InvocationConfiguration;
        List<InputFile<XscnDocumentKind>>? files = InputFiles.Collect(parseResult.GetValue(paths) ?? [], Accepted, invocation.Error, enumerate);
        if (files is null)
        {
            return 2;
        }
        GodotMetadata? loaded = MetadataFile.TryLoad(parseResult.GetValue(metadata), invocation.Error);
        if (loaded is null)
        {
            return 2;
        }
        return Check(files, loaded, invocation);
    }

    // Every file is read before any of them is judged, so a failure part way through leaves no half-reported run.
    private static int Check(List<InputFile<XscnDocumentKind>> files, GodotMetadata metadata, InvocationConfiguration invocation)
    {
        List<(InputFile<XscnDocumentKind> File, string Text)>? read = InputFiles.ReadAll(files, invocation.Error);
        if (read is null)
        {
            return 2;
        }
        int errors = 0;
        int warnings = 0;
        foreach ((InputFile<XscnDocumentKind> file, string text) in read)
        {
            XscnParseResult result = XscnParser.Parse(text, file.Kind, metadata);
            LineMap map = new(text);
            foreach (XscnDiagnostic diagnostic in result.Diagnostics)
            {
                invocation.Output.WriteLine(Format(file.Path, map, diagnostic));
            }
            int fileErrors = result.Diagnostics.Count(diagnostic => diagnostic.Severity == XscnSeverity.Error);
            errors += fileErrors;
            warnings += result.Diagnostics.Length - fileErrors;
        }
        invocation.Output.WriteLine($"{files.Count} file(s) checked, {errors} error(s), {warnings} warning(s)");
        return errors > 0 ? 1 : 0;
    }
}
