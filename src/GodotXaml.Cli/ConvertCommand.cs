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
    private const string NoRootMessage = "no project.godot above this file; its res:// references are not rewritten";

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

    // Every source and every .uid sidecar is read before any file is converted. The run then plans before it writes:
    // which sources it converts, the references their documents hold and the project roots those references live in.
    private static int Convert(List<InputFile<ConvertSource>> files, GodotMetadata metadata, bool overwrite, InvocationConfiguration invocation)
    {
        List<Source>? sources = ReadSources(files, invocation.Error);
        if (sources is null)
        {
            return 2;
        }
        var inputs = new HashSet<string>(sources.Select(source => Path.GetFullPath(source.File.Path)), InputFiles.PathComparer);
        var tally = new Tally { Overwrite = overwrite };
        List<Planned> planned = Plan(sources, inputs, metadata, tally, invocation.Output);
        Dictionary<string, Dictionary<string, string>> renames = Renames(planned, tally, invocation.Output);
        Rewrite(planned, renames, tally, invocation.Output);
        foreach (Planned item in planned)
        {
            Place(item, metadata, tally, invocation);
        }
        ReportSharedUids(planned, tally, invocation.Output);
        ReportFailedWrites(planned, metadata, tally, invocation.Output);
        Scan(planned, tally, invocation);
        invocation.Output.WriteLine($"{tally.Converted} converted, {tally.Refused} refused, {tally.Errors} error(s), {tally.Warnings} warning(s)");
        if (tally.WriteErrors > 0)
        {
            return 2;
        }
        return tally.Errors > 0 || tally.Refused > 0 ? 1 : 0;
    }

    // A pair clash or an existing output refuses a source before any conversion runs; a document the core refuses
    // drops it with its own error. Only the sources left are converted, and only they enter the rename map.
    private static List<Planned> Plan(List<Source> sources, HashSet<string> inputs, GodotMetadata metadata, Tally tally, TextWriter output)
    {
        List<(Source Source, string Output)> candidates = [];
        foreach (Source source in sources)
        {
            string outputPath = Path.ChangeExtension(source.File.Path, source.File.Kind.OutputExtension);
            if (inputs.Contains(Path.GetFullPath(outputPath)))
            {
                // Converting both files of a pair in one run would swap them, so neither is converted.
                output.WriteLine($"{source.File.Path}: {outputPath} is also being converted");
                tally.Refused++;
                continue;
            }
            string? sidecar = source.File.Kind.FromTscn ? outputPath + ".uid" : null;
            string? existing = FirstExisting(outputPath, sidecar);
            if (existing is not null && !tally.Overwrite)
            {
                output.WriteLine($"{source.File.Path}: {existing} exists (use --force)");
                tally.Refused++;
                continue;
            }
            candidates.Add((source, outputPath));
        }
        List<Planned> planned = [];
        foreach ((Source source, string outputPath) in candidates)
        {
            Planned? item = Prepare(source, outputPath, metadata, tally, output);
            if (item is not null)
            {
                planned.Add(item);
            }
        }
        return planned;
    }

    // Converts one source to its document, with no text yet; null when the core refused it or reported an error.
    private static Planned? Prepare(Source source, string outputPath, GodotMetadata metadata, Tally tally, TextWriter output)
    {
        var options = new ConversionOptions { Seed = SeedOf(outputPath), Uid = source.Uid };
        (XscnDocument? Document, string? Uid) prepared;
        try
        {
            prepared = source.File.Kind.FromTscn ? FromTscn(source, metadata, options, tally, output) : FromXscn(source, metadata, tally, output);
        }
        catch (Exception exception) when (CheckCommand.IsCoreRefusal(exception))
        {
            output.WriteLine(CheckCommand.CoreRefusal(source.File.Path, exception));
            tally.Errors++;
            return null;
        }
        if (prepared.Document is null)
        {
            return null;
        }
        var item = new Planned(source, outputPath, prepared.Document, prepared.Uid);
        // The text is produced once with the unrewritten document, so a document the writer refuses or a conversion
        // that reports an error drops the source before the rename map is built from it.
        return Text(item, metadata, tally, output, check: true) is null ? null : item;
    }

    // The rename map of each project root, from every source the run converts under it to its output.
    private static Dictionary<string, Dictionary<string, string>> Renames(List<Planned> planned, Tally tally, TextWriter output)
    {
        var roots = new Dictionary<string, Dictionary<string, string>>(InputFiles.PathComparer);
        foreach (Planned item in planned)
        {
            string? root = ProjectRoot.Find(item.Source.File.Path);
            item.Root = root;
            if (root is null)
            {
                Warn(output, tally, item.Source.File.Path, CliCodes.NoProjectRoot, NoRootMessage);
                continue;
            }
            if (!roots.TryGetValue(root, out Dictionary<string, string>? map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                roots.Add(root, map);
            }
            map[ProjectRoot.ResPath(root, item.Source.File.Path)] = ProjectRoot.ResPath(root, item.Output);
        }
        return roots;
    }

    // Rewrites each document's references to the files this run converts, and reports the text the rewriter could not.
    private static void Rewrite(List<Planned> planned, Dictionary<string, Dictionary<string, string>> renames, Tally tally, TextWriter output)
    {
        foreach (Planned item in planned)
        {
            string? root = item.Root;
            if (root is null)
            {
                continue;
            }
            XscnPathRewriteResult result = XscnPathRewriter.Rewrite(item.Document, renames[root]);
            item.Document = result.Document;
            foreach (string warning in result.Warnings)
            {
                Warn(output, tally, item.Source.File.Path, CliCodes.UnrewrittenPath, warning);
            }
        }
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

    // The existing-output check ran in the plan, so a refused file leaves the disk as it was. The sidecar is written
    // before the output, so an output never stands beside a sidecar of an earlier conversion. A write that fails
    // counts as an error of the run, which goes on with the rest.
    private static void Place(Planned item, GodotMetadata metadata, Tally tally, InvocationConfiguration invocation)
    {
        Converted? converted = Text(item, metadata, tally, invocation.Output, check: false);
        if (converted is null)
        {
            return;
        }
        string? sidecar = item.Source.File.Kind.FromTscn ? item.Output + ".uid" : null;
        byte[]? sidecarBefore = null;
        if (sidecar is not null && !TryPlaceSidecar(sidecar, converted.SidecarUid, invocation.Error, out sidecarBefore))
        {
            WriteFailed(item, tally);
            return;
        }
        if (!InputFiles.TryWrite(item.Output, converted.Text, invocation.Error))
        {
            // The sidecar belongs to the output that was not written, so it goes back to what it was.
            RestoreSidecar(sidecar, sidecarBefore, invocation.Error);
            WriteFailed(item, tally);
            return;
        }
        invocation.Output.WriteLine($"{item.Source.File.Path} -> {item.Output}");
        tally.Converted++;
        item.Written = true;
        item.WrittenText = converted.Text;
    }

    private static void WriteFailed(Planned item, Tally tally)
    {
        tally.Errors++;
        tally.WriteErrors++;
        item.WriteFailed = true;
    }

    // The text to write; null when the core refused the document or a conversion issue is an error, so nothing is
    // written. A check reports only errors, since the run produces the text again and reports every issue then.
    private static Converted? Text(Planned item, GodotMetadata metadata, Tally tally, TextWriter output, bool check)
    {
        try
        {
            if (item.Source.File.Kind.FromTscn)
            {
                return new Converted(XscnWriter.Write(item.Document, metadata), item.Uid);
            }
            var options = new ConversionOptions { Seed = SeedOf(item.Output), Uid = item.Uid };
            ConversionResult<TscnDocument> result = XscnToTscn.Convert(item.Document, metadata, options);
            return Report(item.Source.File.Path, result.Issues, tally, output, errorsOnly: check)
                ? new Converted(TscnWriter.Write(result.Document), SidecarUid: null)
                : null;
        }
        catch (Exception exception) when (CheckCommand.IsCoreRefusal(exception))
        {
            output.WriteLine(CheckCommand.CoreRefusal(item.Source.File.Path, exception));
            tally.Errors++;
            return null;
        }
    }

    // A source keeps the uid its output now carries, so Godot sees two files with one uid until the source is removed.
    private static void ReportSharedUids(List<Planned> planned, Tally tally, TextWriter output)
    {
        foreach (Planned item in planned)
        {
            if (!item.Written || item.Uid is null)
            {
                continue;
            }
            string message =
                $"the source still carries {item.Uid}, which now belongs to {Path.GetFileName(item.Output)}; "
                + "remove the source or Godot will give one of them a new uid";
            Warn(output, tally, item.Source.File.Path, CliCodes.SharedUid, message);
        }
    }

    // A written file that refers to the output of a file this run could not write: the reference would break, and the
    // old file is still there.
    private static void ReportFailedWrites(List<Planned> planned, GodotMetadata metadata, Tally tally, TextWriter output)
    {
        foreach (Planned written in planned.Where(item => item.Written))
        {
            string? root = written.Root;
            if (root is null)
            {
                continue;
            }
            foreach (Planned failed in planned.Where(item => item.WriteFailed && InputFiles.PathComparer.Equals(item.Root, root)))
            {
                string failedPath = ProjectRoot.ResPath(root, failed.Output);
                if (!RefersTo(written.Document, failedPath, metadata))
                {
                    continue;
                }
                string oldPath = ProjectRoot.ResPath(root, failed.Source.File.Path);
                string message = $"refers to {failedPath}, which this run failed to write; {oldPath} still exists";
                (int Line, int Column)? at = FirstOccurrence(written.WrittenText, ReferenceScan.GrammarOf(written.Output), failedPath);
                string path = at is { } location ? Location(written.Output, location.Line, location.Column) : written.Output;
                Warn(output, tally, path, CliCodes.FailedWrite, message);
            }
        }
    }

    // Whether the document refers to a path at all: rewriting it to a probe path changes the document's text only then.
    private static bool RefersTo(XscnDocument document, string path, GodotMetadata metadata)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal) { [path] = "res://.gxp0005-probe/" + Path.GetFileName(path) };
        XscnPathRewriteResult probe = XscnPathRewriter.Rewrite(document, renames);
        return !string.Equals(XscnWriter.Write(probe.Document, metadata), XscnWriter.Write(document, metadata), StringComparison.Ordinal);
    }

    // The first whole quoted occurrence of a path in a text, as its 1-based line and column.
    private static (int Line, int Column)? FirstOccurrence(string text, QuoteGrammar grammar, string path)
    {
        foreach ((int line, int column, string span) in ReferenceScan.Quoted(text, grammar))
        {
            if (string.Equals(span, path, StringComparison.Ordinal))
            {
                return (line, column);
            }
        }
        return null;
    }

    // References to the files this run converts, in the scenes, resources, scripts and project.godot it does not write. A
    // source it could not convert, refused or could not write is not written either, so it is scanned like any other
    // file; its output is not in the map, since the run did not convert it.
    private static void Scan(List<Planned> planned, Tally tally, InvocationConfiguration invocation)
    {
        var skip = new HashSet<string>(InputFiles.PathComparer);
        var roots = new Dictionary<string, Dictionary<string, string>>(InputFiles.PathComparer);
        foreach (Planned item in planned)
        {
            string? root = item.Root;
            if (!item.Written || root is null)
            {
                continue;
            }
            skip.Add(Path.GetFullPath(item.Source.File.Path));
            skip.Add(Path.GetFullPath(item.Output));
            if (!roots.TryGetValue(root, out Dictionary<string, string>? map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                roots.Add(root, map);
            }
            map[ProjectRoot.ResPath(root, item.Source.File.Path)] = ProjectRoot.ResPath(root, item.Output);
        }
        foreach ((string root, Dictionary<string, string> map) in roots)
        {
            foreach (ScannedReference reference in ReferenceScan.Find(root, map, skip, invocation.Error))
            {
                string path = Location(reference.File, reference.Line, reference.Column);
                string code = reference.Kind == ScriptHitKind.Whole ? CliCodes.UntouchedReference : CliCodes.BuiltPath;
                Warn(invocation.Output, tally, path, code, ReferenceMessage(reference));
            }
        }
    }

    private static string ReferenceMessage(ScannedReference reference)
    {
        if (reference.Kind == ScriptHitKind.Inside)
        {
            return $"holds {reference.Key}, which this run converts to {reference.Value}, inside a longer string; it is not rewritten";
        }
        if (reference.Kind == ScriptHitKind.Fragment)
        {
            return $"ends in {reference.Key}, and this run converts such files to {reference.Value}; a path built from it is not rewritten";
        }
        string clause = reference.CarriesUid
            ? "still resolves by its uid once the source is removed and the uid moves with the file"
            : "no uid on this reference; it breaks once the source is removed";
        return $"refers to {reference.Key}, which this run converts to {reference.Value}; {clause}";
    }

    private static void Warn(TextWriter output, Tally tally, string path, string code, string message)
    {
        output.WriteLine($"{path}: warning {code}: {message}");
        tally.Warnings++;
    }

    private static string Location(string path, int line, int column) => $"{path}({line},{column})";

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
    private static (XscnDocument? Document, string? Uid) FromTscn(
        Source source,
        GodotMetadata metadata,
        ConversionOptions options,
        Tally tally,
        TextWriter output
    )
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
            return (null, null);
        }
        XscnDocumentKind held = document.Kind == TscnKind.Scene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource;
        if (held != source.File.Kind.Kind)
        {
            output.WriteLine($"{source.File.Path}: error GXT0003: {KindMismatch(held)}");
            tally.Errors++;
            return (null, null);
        }
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(document, metadata, options);
        if (!Report(source.File.Path, result.Issues, tally, output, errorsOnly: false))
        {
            return (null, null);
        }
        return (result.Document, document.Header.Uid);
    }

    private static string KindMismatch(XscnDocumentKind held) =>
        held == XscnDocumentKind.Scene ? "a .tres file holds a scene." : "a .tscn file holds a resource.";

    // The parsed document, which the run rewrites before the way back converts it, so its ext_resource ids follow the
    // final paths.
    private static (XscnDocument? Document, string? Uid) FromXscn(Source source, GodotMetadata metadata, Tally tally, TextWriter output)
    {
        XscnParseResult parsed = XscnParser.Parse(source.Text, source.File.Kind.Kind, metadata);
        LineMap map = new(source.Text);
        foreach (XscnDiagnostic diagnostic in parsed.Diagnostics)
        {
            output.WriteLine(CheckCommand.Format(source.File.Path, map, diagnostic));
            tally.Count(diagnostic.Severity == XscnSeverity.Error);
        }
        return parsed.HasErrors ? (null, null) : (parsed.Document, source.Uid);
    }

    // Prints each issue; false when one is an error, so the file is not written. With errorsOnly, a warning is neither
    // printed nor counted, since another pass reports it.
    private static bool Report(string path, IEnumerable<ConversionIssue> issues, Tally tally, TextWriter output, bool errorsOnly)
    {
        bool clean = true;
        foreach (ConversionIssue issue in issues)
        {
            bool isError = issue.Severity == ConversionSeverity.Error;
            clean &= !isError;
            if (isError || !errorsOnly)
            {
                string severity = isError ? "error" : "warning";
                output.WriteLine($"{path}: {severity} {issue.Code}: {issue.Message} ({issue.Location})");
                tally.Count(isError);
            }
        }
        return clean;
    }

    private sealed record Source(InputFile<ConvertSource> File, string Text, string? Uid);

    // A source the run will convert: where its output goes, the document to write, the uid the output carries, the
    // project root its references are rewritten within, whether it was written and the text written for it.
    private sealed class Planned(Source source, string output, XscnDocument document, string? uid)
    {
        public Source Source { get; } = source;

        public string Output { get; } = output;

        public XscnDocument Document { get; set; } = document;

        public string? Uid { get; } = uid;

        public string? Root { get; set; }

        public bool Written { get; set; }

        public string WrittenText { get; set; } = "";

        public bool WriteFailed { get; set; }
    }

    // The text to write, and for a .xscn or .xres the uid its .uid sidecar holds (null for none).
    private sealed record Converted(string Text, string? SidecarUid);

    // The run's overwrite setting and its counts.
    private sealed class Tally
    {
        public bool Overwrite { get; init; }

        public int Converted { get; set; }

        public int Refused { get; set; }

        public int Errors { get; set; }

        public int Warnings { get; set; }

        public int WriteErrors { get; set; }

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
