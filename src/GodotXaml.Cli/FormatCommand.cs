using System.CommandLine;
using System.CommandLine.Invocation;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>The <c>format</c> command: rewrites each file in the canonical layout, or with <c>--check</c> names each that would change.</summary>
internal static class FormatCommand
{
    private static readonly AcceptedFiles<XscnDocumentKind> Accepted = new(InputFiles.XscnExtensions, "only .xscn and .xres files can be formatted.");

    /// <summary>The command, its paths argument and its options.</summary>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <param name="versions">The Godot versions this run carries, and how it loads one's metadata.</param>
    /// <returns>The command.</returns>
    internal static Command Create(Func<string, IEnumerable<string>> enumerate, MetadataVersions versions)
    {
        Argument<string[]> paths = CheckCommand.PathsArgument("A .xscn or .xres file, or a folder searched recursively for both.");
        Option<string> metadata = CheckCommand.MetadataOption();
        Option<bool> check = new("--check") { Description = "Changes nothing; names each file that would change, and exits 1 when any would." };
        var command = new Command("format", "Rewrites .xscn and .xres files in place in the canonical layout.");
        command.Arguments.Add(paths);
        command.Options.Add(metadata);
        command.Options.Add(check);
        command.SetAction(parseResult =>
        {
            InvocationConfiguration invocation = parseResult.InvocationConfiguration;
            List<InputFile<XscnDocumentKind>>? files = InputFiles.Collect(parseResult.GetValue(paths) ?? [], Accepted, invocation.Error, enumerate);
            if (files is null)
            {
                return 2;
            }
            var choice = MetadataChoice.For(parseResult.GetValue(metadata), versions, invocation.Error, invocation.Output);
            if (choice is null)
            {
                return 2;
            }
            return Format(files, choice, parseResult.GetValue(check), invocation);
        });
        return command;
    }

    private static int Format(List<InputFile<XscnDocumentKind>> files, MetadataChoice choice, bool checkOnly, InvocationConfiguration invocation)
    {
        List<(InputFile<XscnDocumentKind> File, string Text)>? read = InputFiles.ReadAll(files, invocation.Error);
        if (read is null)
        {
            return 2;
        }
        List<FileOutcome> outcomes = [];
        foreach ((InputFile<XscnDocumentKind> file, string text) in read)
        {
            FileOutcome outcome = FormatOne(file, text, choice, checkOnly, invocation);
            if (outcome == FileOutcome.WriteFailed)
            {
                return 2;
            }
            outcomes.Add(outcome);
        }
        return Summarize(outcomes, checkOnly, invocation.Output);
    }

    // Exit 1 when a file had an error, or under --check when any would change.
    private static int Summarize(List<FileOutcome> outcomes, bool checkOnly, TextWriter output)
    {
        int changed = outcomes.Count(outcome => outcome == FileOutcome.Changed);
        string verb = checkOnly ? "would change" : "changed";
        output.WriteLine($"{changed} of {outcomes.Count} file(s) {verb}");
        bool failed = outcomes.Contains(FileOutcome.Error) || (checkOnly && changed > 0);
        return failed ? 1 : 0;
    }

    // Warnings are printed and do not stop the rewrite; an error leaves the file as it is.
    private static FileOutcome FormatOne(
        InputFile<XscnDocumentKind> file,
        string text,
        MetadataChoice choice,
        bool checkOnly,
        InvocationConfiguration invocation
    )
    {
        string? canonical = CanonicalOf(file, text, choice, invocation.Output);
        if (canonical is null)
        {
            return FileOutcome.Error;
        }
        if (string.Equals(canonical, text, StringComparison.Ordinal))
        {
            return FileOutcome.Unchanged;
        }
        if (checkOnly)
        {
            invocation.Output.WriteLine($"{file.Path}: not canonical");
            return FileOutcome.Changed;
        }
        if (!InputFiles.TryWrite(file.Path, canonical, invocation.Error))
        {
            return FileOutcome.WriteFailed;
        }
        invocation.Output.WriteLine($"formatted {file.Path}");
        return FileOutcome.Changed;
    }

    // The file's canonical text, or null when it has an error, the choice refuses to write it, or the writer refuses it;
    // every problem is printed.
    private static string? CanonicalOf(InputFile<XscnDocumentKind> file, string text, MetadataChoice choice, TextWriter output)
    {
        GodotMetadata metadata = choice.Of(file.Path, text).Metadata;
        XscnParseResult result = XscnParser.Parse(text, file.Kind, metadata);
        LineMap map = new(text);
        foreach (XscnDiagnostic diagnostic in result.Diagnostics)
        {
            output.WriteLine(CheckCommand.Format(file.Path, map, diagnostic));
        }
        if (result.HasErrors || !choice.CanWrite(file.Path, text))
        {
            return null;
        }
        try
        {
            return XscnWriter.Write(result.Document, metadata);
        }
        catch (Exception exception) when (WriterRefusal.Is(exception))
        {
            output.WriteLine(CheckCommand.CoreRefusal(file.Path, exception));
            return null;
        }
    }

    private enum FileOutcome
    {
        Unchanged,
        Changed,
        Error,
        WriteFailed,
    }
}
