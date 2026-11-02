using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>What a source's extension tells <c>convert</c>: the direction, the document kind and the output's extension.</summary>
/// <param name="FromTscn">Whether the source is a .tscn or .tres, converted to .xscn or .xres; else the way back.</param>
/// <param name="Kind">Scene or resource.</param>
/// <param name="OutputExtension">The output's extension, with its leading dot.</param>
internal sealed record ConvertSource(bool FromTscn, XscnDocumentKind Kind, string OutputExtension);

/// <summary>
/// The <c>convert</c> command: writes each file beside its source with the other extension, the direction following the
/// source's extension. An output that exists is refused unless <c>--force</c> or <c>godot-xaml.json</c> allows overwriting.
/// </summary>
internal static class ConvertCommand
{
    private static readonly AcceptedFiles<ConvertSource> Accepted = new(
        [
            new(".tscn", new ConvertSource(FromTscn: true, XscnDocumentKind.Scene, ".xscn")),
            new(".tres", new ConvertSource(FromTscn: true, XscnDocumentKind.Resource, ".xres")),
            new(".xscn", new ConvertSource(FromTscn: false, XscnDocumentKind.Scene, ".tscn")),
            new(".xres", new ConvertSource(FromTscn: false, XscnDocumentKind.Resource, ".tres")),
        ],
        "only .tscn, .tres, .xscn and .xres files can be converted."
    );

    /// <summary>The command, its paths argument and its options.</summary>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <param name="workingDirectory">The folder the search for <c>godot-xaml.json</c> starts from.</param>
    /// <returns>The command.</returns>
    internal static Command Create(Func<string, IEnumerable<string>> enumerate, string workingDirectory)
    {
        Argument<string[]> paths = CheckCommand.PathsArgument("A .tscn, .tres, .xscn or .xres file, or a folder searched recursively for all four.");
        Option<string> metadata = CheckCommand.MetadataOption();
        Option<bool> force = new("--force") { Description = "Overwrites an output that already exists." };
        var command = new Command("convert", "Converts .tscn and .tres files to .xscn and .xres, and back, writing each beside its source.");
        command.Arguments.Add(paths);
        command.Options.Add(metadata);
        command.Options.Add(force);
        command.SetAction(parseResult =>
        {
            InvocationConfiguration invocation = parseResult.InvocationConfiguration;
            List<InputFile<ConvertSource>>? files = InputFiles.Collect(parseResult.GetValue(paths) ?? [], Accepted, invocation.Error, enumerate);
            if (files is null)
            {
                return 2;
            }
            GodotMetadata? loaded = MetadataFile.TryLoad(parseResult.GetValue(metadata), invocation.Error);
            if (loaded is null)
            {
                return 2;
            }
            bool? configOverwrite = ProjectConfig.TryReadOverwrite(workingDirectory, invocation.Error);
            if (configOverwrite is null)
            {
                return 2;
            }
            return Convert(files, loaded, parseResult.GetValue(force) || configOverwrite.Value, invocation);
        });
        return command;
    }

    /// <summary>The seed of an output's new resource ids: FNV-1a over its full path's UTF-8, the same on every run.</summary>
    /// <param name="outputPath">The output's path.</param>
    /// <returns>The seed.</returns>
    internal static ulong SeedOf(string outputPath)
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        ulong hash = OffsetBasis;
        foreach (byte value in Encoding.UTF8.GetBytes(Path.GetFullPath(outputPath)))
        {
            hash = unchecked((hash ^ value) * Prime);
        }
        return hash;
    }

    // Every source and every .uid sidecar is read before any file is converted.
    private static int Convert(List<InputFile<ConvertSource>> files, GodotMetadata metadata, bool overwrite, InvocationConfiguration invocation)
    {
        List<Source>? sources = ReadSources(files, invocation.Error);
        if (sources is null)
        {
            return 2;
        }
        var inputs = new HashSet<string>(sources.Select(source => Path.GetFullPath(source.File.Path)), InputFiles.PathComparer);
        var tally = new Tally { Overwrite = overwrite };
        foreach (Source source in sources)
        {
            string output = Path.ChangeExtension(source.File.Path, source.File.Kind.OutputExtension);
            if (inputs.Contains(Path.GetFullPath(output)))
            {
                // Converting both files of a pair in one run would swap them, so neither is converted.
                invocation.Output.WriteLine($"{source.File.Path}: {output} is also being converted");
                tally.Refused++;
            }
            else if (!ConvertOne(source, output, metadata, tally, invocation))
            {
                return 2;
            }
        }
        invocation.Output.WriteLine($"{tally.Converted} converted, {tally.Refused} refused, {tally.Errors} error(s), {tally.Warnings} warning(s)");
        return tally.Errors > 0 || tally.Refused > 0 ? 1 : 0;
    }

    private static List<Source>? ReadSources(List<InputFile<ConvertSource>> files, TextWriter error)
    {
        List<(InputFile<ConvertSource> File, string Text)>? read = InputFiles.ReadAll(files, error);
        if (read is null)
        {
            return null;
        }
        List<Source> sources = [];
        foreach ((InputFile<ConvertSource> file, string text) in read)
        {
            if (!TryReadSidecarUid(file, error, out string? uid))
            {
                return null;
            }
            sources.Add(new Source(file, text, uid));
        }
        return sources;
    }

    // An .xscn or .xres keeps its uid in the .uid sidecar beside it; its first line, trimmed, is the uid.
    private static bool TryReadSidecarUid(InputFile<ConvertSource> file, TextWriter error, out string? uid)
    {
        uid = null;
        string sidecar = file.Path + ".uid";
        if (file.Kind.FromTscn || !File.Exists(sidecar))
        {
            return true;
        }
        string? text = SourceText.Read(sidecar, error);
        if (text is null)
        {
            return false;
        }
        string first = text.Split('\n', 2)[0].Trim();
        uid = first.Length > 0 ? first : null;
        return true;
    }

    // False only when an output could not be written, which stops the run. A file the core refuses is an error of its
    // own, and the run goes on to the next file.
    private static bool ConvertOne(Source source, string output, GodotMetadata metadata, Tally tally, InvocationConfiguration invocation)
    {
        var options = new ConversionOptions { Seed = SeedOf(output), Uid = source.Uid };
        Converted? converted;
        try
        {
            converted = source.File.Kind.FromTscn
                ? FromTscn(source, metadata, options, tally, invocation.Output)
                : FromXscn(source, metadata, options, tally, invocation.Output);
        }
        catch (Exception exception) when (CheckCommand.IsCoreRefusal(exception))
        {
            invocation.Output.WriteLine(CheckCommand.CoreRefusal(source.File.Path, exception));
            tally.Errors++;
            return true;
        }
        return converted is null || Place(source.File.Path, output, converted, tally, invocation);
    }

    // The existing-output check runs before anything of the file is written, so a refused file leaves the disk as it was.
    // The sidecar is written before the output, so an output never stands beside a sidecar of an earlier conversion.
    private static bool Place(string path, string output, Converted converted, Tally tally, InvocationConfiguration invocation)
    {
        string? sidecar = converted.KeepsUidInSidecar ? output + ".uid" : null;
        string? existing = FirstExisting(output, sidecar);
        if (existing is not null && !tally.Overwrite)
        {
            invocation.Output.WriteLine($"{path}: {existing} exists (use --force)");
            tally.Refused++;
            return true;
        }
        byte[]? sidecarBefore = null;
        if (sidecar is not null && !TryPlaceSidecar(sidecar, converted.SidecarUid, invocation.Error, out sidecarBefore))
        {
            return false;
        }
        if (!InputFiles.TryWrite(output, converted.Text, invocation.Error))
        {
            // The sidecar belongs to the output that was not written, so it goes back to what it was.
            RestoreSidecar(sidecar, sidecarBefore, invocation.Error);
            return false;
        }
        invocation.Output.WriteLine($"{path} -> {output}");
        tally.Converted++;
        return true;
    }

    // A conversion without a uid removes a sidecar left from an earlier one, which would give the output a uid it lacks.
    // The sidecar's bytes before the step are kept, null when there was none, so the step can be undone.
    private static bool TryPlaceSidecar(string sidecar, string? uid, TextWriter error, out byte[]? before)
    {
        before = null;
        if (File.Exists(sidecar) && !TryReadBytes(sidecar, error, out before))
        {
            return false;
        }
        if (uid is not null)
        {
            return InputFiles.TryWrite(sidecar, uid + "\n", error);
        }
        return before is null || InputFiles.TryDelete(sidecar, error);
    }

    private static void RestoreSidecar(string? sidecar, byte[]? before, TextWriter error)
    {
        if (sidecar is null)
        {
            return;
        }
        if (before is not null)
        {
            InputFiles.TryWrite(sidecar, before, error);
        }
        else if (File.Exists(sidecar))
        {
            InputFiles.TryDelete(sidecar, error);
        }
    }

    private static bool TryReadBytes(string path, TextWriter error, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        try
        {
            bytes = File.ReadAllBytes(path);
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be read.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
        }
        return false;
    }

    private static string? FirstExisting(string output, string? sidecar)
    {
        if (File.Exists(output))
        {
            return output;
        }
        return sidecar is not null && File.Exists(sidecar) ? sidecar : null;
    }

    // The header's uid goes to the output's .uid sidecar, since the .xscn itself holds none.
    private static Converted? FromTscn(Source source, GodotMetadata metadata, ConversionOptions options, Tally tally, TextWriter output)
    {
        TscnDocument document;
        try
        {
            document = TscnReader.Read(source.Text);
        }
        catch (TscnParseException exception)
        {
            output.WriteLine($"{source.File.Path}({exception.Line},{exception.Column}): error GXT0001: {exception.Reason}");
            tally.Errors++;
            return null;
        }
        XscnDocumentKind held = document.Kind == TscnKind.Scene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource;
        if (held != source.File.Kind.Kind)
        {
            output.WriteLine($"{source.File.Path}: error GXT0003: {KindMismatch(held)}");
            tally.Errors++;
            return null;
        }
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(document, metadata, options);
        if (!Report(source.File.Path, result.Issues, tally, output))
        {
            return null;
        }
        return new Converted(XscnWriter.Write(result.Document, metadata), document.Header.Uid, KeepsUidInSidecar: true);
    }

    private static string KindMismatch(XscnDocumentKind held) =>
        held == XscnDocumentKind.Scene ? "a .tres file holds a scene." : "a .tscn file holds a resource.";

    private static Converted? FromXscn(Source source, GodotMetadata metadata, ConversionOptions options, Tally tally, TextWriter output)
    {
        XscnParseResult parsed = XscnParser.Parse(source.Text, source.File.Kind.Kind, metadata);
        LineMap map = new(source.Text);
        foreach (XscnDiagnostic diagnostic in parsed.Diagnostics)
        {
            output.WriteLine(CheckCommand.Format(source.File.Path, map, diagnostic));
            tally.Count(diagnostic.Severity == XscnSeverity.Error);
        }
        if (parsed.HasErrors)
        {
            return null;
        }
        ConversionResult<TscnDocument> result = XscnToTscn.Convert(parsed.Document, metadata, options);
        if (!Report(source.File.Path, result.Issues, tally, output))
        {
            return null;
        }
        return new Converted(TscnWriter.Write(result.Document), SidecarUid: null, KeepsUidInSidecar: false);
    }

    // Prints each issue; false when one is an error, so the file is not written.
    private static bool Report(string path, IEnumerable<ConversionIssue> issues, Tally tally, TextWriter output)
    {
        bool clean = true;
        foreach (ConversionIssue issue in issues)
        {
            bool isError = issue.Severity == ConversionSeverity.Error;
            string severity = isError ? "error" : "warning";
            output.WriteLine($"{path}: {severity} {issue.Code}: {issue.Message} ({issue.Location})");
            tally.Count(isError);
            clean &= !isError;
        }
        return clean;
    }

    private sealed record Source(InputFile<ConvertSource> File, string Text, string? Uid);

    // The text to write, and for a .xscn or .xres the uid its .uid sidecar holds (null for none).
    private sealed record Converted(string Text, string? SidecarUid, bool KeepsUidInSidecar);

    // The run's overwrite setting and its counts.
    private sealed class Tally
    {
        public bool Overwrite { get; init; }

        public int Converted { get; set; }

        public int Refused { get; set; }

        public int Errors { get; set; }

        public int Warnings { get; set; }

        public void Count(bool isError)
        {
            if (isError)
            {
                Errors++;
            }
            else
            {
                Warnings++;
            }
        }
    }
}
