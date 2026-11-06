using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Projects;
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
/// With <c>--project</c> it converts a whole project or changes nothing, then edits its scripts and <c>project.godot</c>
/// and removes the originals.
/// </summary>
internal static class ConvertCommand
{
    private const string NoRootMessage = "no project.godot above this file; its res:// references are not rewritten";

    // A file's built paths past the third each add a line without saying anything new, so the fourth says how many more
    // there are in that file and the rest say nothing.
    private const int RolledUpAt = 4;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly AcceptedFiles<ConvertSource> Accepted = new(
        [
            new(".tscn", new ConvertSource(FromTscn: true, XscnDocumentKind.Scene, ".xscn")),
            new(".tres", new ConvertSource(FromTscn: true, XscnDocumentKind.Resource, ".xres")),
            new(".xscn", new ConvertSource(FromTscn: false, XscnDocumentKind.Scene, ".tscn")),
            new(".xres", new ConvertSource(FromTscn: false, XscnDocumentKind.Resource, ".tres")),
        ],
        "only .tscn, .tres, .xscn and .xres files can be converted."
    );

    private static readonly string[] ForwardExtensions = [".tscn", ".tres"];
    private static readonly string[] BackExtensions = [".xscn", ".xres"];
    private static readonly string[] ScriptExtensions = [".gd", ".cs"];

    /// <summary>The command, its paths argument and its options.</summary>
    /// <param name="enumerate">The files directly in a folder.</param>
    /// <param name="workingDirectory">The folder the search for <c>godot-xaml.json</c> starts from.</param>
    /// <param name="versions">The Godot versions this run carries, and how it loads one's metadata.</param>
    /// <returns>The command.</returns>
    internal static Command Create(Func<string, IEnumerable<string>> enumerate, string workingDirectory, MetadataVersions versions)
    {
        var options = new ConvertOptions(
            CheckCommand.PathsArgument("A .tscn, .tres, .xscn or .xres file, or a folder searched recursively for all four."),
            CheckCommand.MetadataOption(),
            new Option<bool>("--force") { Description = "Overwrites an output that already exists." },
            new Option<string>("--project")
            {
                Description =
                    "A Godot project's root folder: converts every .tscn and .tres under it, or nothing if one fails, edits the "
                    + "scripts and project.godot that name them, and removes the originals after backing them up.",
            },
            new Option<bool>("--back") { Description = "With --project, converts every .xscn and .xres back to .tscn and .tres." },
            new Option<bool>("--nobackup") { Description = "Deletes the originals with no backup." }
        );
        options.Paths.Arity = ArgumentArity.ZeroOrMore;
        var command = new Command("convert", "Converts .tscn and .tres files to .xscn and .xres, and back, writing each beside its source.");
        command.Arguments.Add(options.Paths);
        command.Options.Add(options.Metadata);
        command.Options.Add(options.Force);
        command.Options.Add(options.Project);
        command.Options.Add(options.Back);
        command.Options.Add(options.NoBackup);
        command.SetAction(parseResult => Run(parseResult, options, enumerate, workingDirectory, versions));
        return command;
    }

    private static int Run(
        ParseResult parseResult,
        ConvertOptions options,
        Func<string, IEnumerable<string>> enumerate,
        string workingDirectory,
        MetadataVersions versions
    )
    {
        InvocationConfiguration invocation = parseResult.InvocationConfiguration;
        string? root = parseResult.GetValue(options.Project);
        string[] paths = parseResult.GetValue(options.Paths) ?? [];
        string? usage = root is null ? RootlessUsageError(parseResult, options, paths.Length > 0) : ProjectUsageError(root, paths.Length > 0);
        if (usage is not null)
        {
            invocation.Error.WriteLine(usage);
            return 2;
        }
        ProjectSettings? project = root is null
            ? null
            : new ProjectSettings(FullRoot(root), parseResult.GetValue(options.NoBackup), parseResult.GetValue(options.Back));
        List<InputFile<ConvertSource>>? files = project is null
            ? InputFiles.Collect(paths, Accepted, invocation.Error, enumerate)
            : ProjectSources(project.Root, project.Back, invocation.Output, invocation.Error);
        return files is null ? 2 : LoadAndConvert(parseResult, options, files, project, workingDirectory, versions);
    }

    private static string FullRoot(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private static int LoadAndConvert(
        ParseResult parseResult,
        ConvertOptions options,
        List<InputFile<ConvertSource>> files,
        ProjectSettings? project,
        string workingDirectory,
        MetadataVersions versions
    )
    {
        InvocationConfiguration invocation = parseResult.InvocationConfiguration;
        var choice = MetadataChoice.For(parseResult.GetValue(options.Metadata), versions, workingDirectory, invocation.Error, invocation.Output);
        if (choice is null)
        {
            return 2;
        }
        bool? configOverwrite = ProjectConfig.TryReadOverwrite(workingDirectory, invocation.Error);
        if (configOverwrite is null)
        {
            return 2;
        }
        return Convert(files, choice, parseResult.GetValue(options.Force) || configOverwrite.Value, project, invocation);
    }

    private static string? RootlessUsageError(ParseResult parseResult, ConvertOptions options, bool hasPaths)
    {
        if (!hasPaths)
        {
            return "convert needs a path or --project.";
        }
        if (parseResult.GetValue(options.Back))
        {
            return "--back needs --project.";
        }
        return parseResult.GetValue(options.NoBackup) ? "--nobackup needs --project." : null;
    }

    private static string? ProjectUsageError(string root, bool hasPaths)
    {
        if (hasPaths)
        {
            return "--project takes no paths.";
        }
        return File.Exists(Path.Combine(root, "project.godot")) ? null : $"{FullRoot(root)}: no project.godot in this folder.";
    }

    // Every scene and resource of the direction under the root, addons/ left out with the folders Godot leaves out, and
    // the files Godot loads before any addon left as text.
    private static List<InputFile<ConvertSource>>? ProjectSources(string root, bool back, TextWriter output, TextWriter error)
    {
        List<string>? found = ReferenceScan.ProjectFiles(root, back ? BackExtensions : ForwardExtensions, skipAddons: true, error);
        if (found is null)
        {
            return null;
        }
        Leave(back ? [] : StartupFiles.Find(root, found), found, output);
        List<InputFile<ConvertSource>> files = [];
        foreach (string path in found)
        {
            if (Accepted.TryGetKind(path, out ConvertSource? kind))
            {
                files.Add(new InputFile<ConvertSource>(path, kind));
            }
        }
        return files;
    }

    // Each file Godot loads before any addon is named on a line of its own, which the summary does not count, and is
    // left out of the run: it is not read, converted, renamed, backed up or removed, and project.godot is not edited
    // for it. A file whose references could not be followed says so on the next line, and one outside the run's sources
    // is only walked, so it is not named as left. When every file is left, the run reaches its no-files line.
    private static void Leave(List<LeftFile> left, List<string> found, TextWriter output)
    {
        if (left.Count == 0)
        {
            return;
        }
        var leftPaths = new HashSet<string>(InputFiles.PathComparer);
        foreach (LeftFile file in left)
        {
            if (file.Listed)
            {
                output.WriteLine($"{file.Path}: left as text: Godot loads it before any addon ({file.Reason})");
                leftPaths.Add(Path.GetFullPath(file.Path));
            }
            if (file.Unread is not null)
            {
                output.WriteLine($"{file.Path}: its references were not followed: {file.Unread}");
            }
        }
        found.RemoveAll(path => leftPaths.Contains(Path.GetFullPath(path)));
    }

    /// <summary>The seed of an output's new resource ids: FNV-1a 64 over the UTF-8 of its key, as given.</summary>
    /// <param name="key">The output's seed key (<see cref="SeedKeyOf"/>).</param>
    /// <returns>The seed.</returns>
    internal static ulong SeedOf(string key)
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        ulong hash = OffsetBasis;
        foreach (byte value in Encoding.UTF8.GetBytes(key))
        {
            hash = unchecked((hash ^ value) * Prime);
        }
        return hash;
    }

    // The key an output's new ids are seeded from: the source's res:// path in its on-disk casing with the output's
    // extension, so one project converts to the same ids wherever it is checked out. A source with no project.godot
    // above it has no res:// path, so its output's full path stands in.
    private static string SeedKeyOf(string sourcePath, string outputPath)
    {
        string? root = GodotProject.Find(sourcePath);
        return root is null
            ? Path.GetFullPath(outputPath)
            : Path.ChangeExtension(GodotProject.ResPath(root, sourcePath), Path.GetExtension(outputPath));
    }

    // Every source and every .uid sidecar is read before any file is converted. The run then plans before it writes:
    // which sources it converts, the references their documents hold and the project roots those references live in.
    private static int Convert(
        List<InputFile<ConvertSource>> files,
        MetadataChoice choice,
        bool overwrite,
        ProjectSettings? project,
        InvocationConfiguration invocation
    )
    {
        List<Source>? sources = ReadSources(files, invocation.Error);
        if (sources is null)
        {
            return 2;
        }
        var inputs = new HashSet<string>(sources.Select(source => Path.GetFullPath(source.File.Path)), InputFiles.PathComparer);
        var tally = new Tally
        {
            Overwrite = overwrite,
            Project = project is not null,
            SceneFiles = new RunSceneFiles(sources.Select(source => (source.File.Path, source.Text))),
        };
        List<Planned> planned = Plan(sources, inputs, choice, tally, invocation.Output);
        tally.SceneFiles.Converting(planned.Select(item => (item.Source.File.Path, item.Output)));
        Dictionary<string, Dictionary<string, string>> renames = Renames(planned, tally, invocation.Output);
        Rewrite(planned, renames, tally, invocation.Output);
        if (project is null)
        {
            FinishPlain(planned, tally, invocation);
        }
        else
        {
            Dictionary<string, string> map = renames.GetValueOrDefault(project.Root) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            FinishProject(new ProjectRun(project, tally, invocation), sources.Count, planned, map);
        }
        invocation.Output.WriteLine($"{tally.Converted} converted, {tally.Refused} refused, {tally.Errors} error(s), {tally.Warnings} warning(s)");
        if (tally.WriteErrors > 0)
        {
            return 2;
        }
        return tally.Errors > 0 || tally.Refused > 0 ? 1 : 0;
    }

    private static void FinishPlain(List<Planned> planned, Tally tally, InvocationConfiguration invocation)
    {
        foreach (Planned item in planned)
        {
            Place(item, tally, invocation);
        }
        ReportSharedUids(planned, tally, invocation.Output);
        ReportFailedWrites(planned, tally, invocation.Output);
        Scan(planned, tally, invocation, []);
    }

    // All or nothing: every output is written to a staging folder first. When a source was not converted or staged,
    // the staging folder goes and the project is as it was. Otherwise the originals are backed up, the outputs moved
    // in, the scripts edited and the originals removed, in that order. The staging folder never outlives the run. A
    // project with no source makes no backup and says so.
    private static void FinishProject(ProjectRun run, int sourceCount, List<Planned> planned, IReadOnlyDictionary<string, string> renames)
    {
        if (sourceCount == 0)
        {
            run.Invocation.Output.WriteLine($"{run.Root}: no {(run.Back ? ".xscn or .xres" : ".tscn or .tres")} files to convert");
            return;
        }
        try
        {
            bool converted = Stage(run, planned) && planned.Count == sourceCount;
            List<ScriptEdit>? edits = FindEdits(run, renames);
            if (!converted || edits is null)
            {
                // A failed run moves no output in, so every source found counts as not converted.
                string message = $"nothing changed: {sourceCount} source(s) were not converted; the project is as it was";
                Warn(run.Invocation.Output, run.Tally, run.Root, CliCodes.OriginalsKept, message, 1);
                return;
            }
            if (Apply(run, planned, edits))
            {
                Scan(planned, run.Tally, run.Invocation, run.NotUtf8);
            }
        }
        finally
        {
            run.Staging.Dispose();
        }
    }

    // Writes every output to the staging folder, each at its path under the root; true when every one was written.
    private static bool Stage(ProjectRun run, List<Planned> planned)
    {
        if (planned.Count == 0)
        {
            return true;
        }
        if (!run.Staging.TryCreate())
        {
            WriteError(run.Tally);
            return false;
        }
        foreach (Planned item in planned)
        {
            item.Staged = run.Staging.PathOf(item.Output);
            Place(item, run.Tally, run.Invocation);
        }
        return planned.All(item => item.Written);
    }

    // The steps that change the project. A failure from the first move on stops at its step, leaves the originals in
    // place and says where the backup is; nothing is rolled back.
    private static bool Apply(ProjectRun run, List<Planned> planned, List<ScriptEdit> edits)
    {
        if (!run.NoBackup && !BackUp(run, planned, edits))
        {
            WriteError(run.Tally);
            return false;
        }
        if (MoveIn(run, planned) && WriteEdits(run, edits) && RemoveOriginals(run, planned))
        {
            return true;
        }
        WriteError(run.Tally);
        string message = run.NoBackup
            ? $"{run.Root}: the project is part converted, and --nobackup kept no copy"
            : $"{run.Root}: the project is part converted; restore it from {run.Backup}";
        run.Invocation.Error.WriteLine(message);
        return false;
    }

    // Every script and project.godot under the root, addons/ left out, with its whole-literal references to a converted
    // file rewritten; only the files that change, and nothing written yet. Null when one cannot be read, a folder cannot
    // be walked, or a file to edit is read-only; every such file is reported before the run stops.
    private static List<ScriptEdit>? FindEdits(ProjectRun run, IReadOnlyDictionary<string, string> renames)
    {
        if (renames.Count == 0)
        {
            return [];
        }
        List<string>? scripts = ReferenceScan.ProjectFiles(run.Root, ScriptExtensions, skipAddons: true, run.Invocation.Error);
        if (scripts is null)
        {
            WriteError(run.Tally);
            return null;
        }
        scripts.Add(Path.Combine(run.Root, "project.godot"));
        List<ScriptEdit> edits = [];
        bool editable = true;
        foreach (string file in scripts)
        {
            editable &= TryAddEdit(run, file, renames, edits);
        }
        return editable ? edits : null;
    }

    // False when the file cannot be read, a write error, or is read-only and would be edited, a refusal. A file that is
    // not UTF-8 is warned and left; a file with nothing to rewrite adds no edit.
    private static bool TryAddEdit(ProjectRun run, string file, IReadOnlyDictionary<string, string> renames, List<ScriptEdit> edits)
    {
        if (!TryReadBytes(file, run.Invocation.Error, out byte[]? bytes))
        {
            WriteError(run.Tally);
            return false;
        }
        string? text = DecodeUtf8(bytes);
        if (text is null)
        {
            Warn(run.Invocation.Output, run.Tally, file, CliCodes.NotUtf8, "not UTF-8 text; not scanned or edited", 1);
            run.NotUtf8.Add(file);
            return true;
        }
        ScriptRewrite rewrite = ScriptPaths.Rewrite(text, ScriptPaths.KindOf(file)!.Value, renames);
        if (rewrite.Rewritten == 0)
        {
            return true;
        }
        if (IsReadOnlyTarget(file))
        {
            run.Invocation.Output.WriteLine($"{file}: read-only, so it cannot be edited");
            run.Tally.Refused++;
            return false;
        }
        // A leading byte order mark is kept, since the rewriter works on the text after it.
        byte[] written = Encoding.UTF8.GetBytes(rewrite.Text);
        byte[] encoded = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? [.. Encoding.UTF8.Preamble, .. written] : written;
        edits.Add(new ScriptEdit(file, encoded, bytes, rewrite.Rewritten));
        return true;
    }

    // The text after a UTF-8 byte order mark, or null when the bytes are not UTF-8, which the caller reports.
    private static string? DecodeUtf8(byte[] bytes)
    {
        ReadOnlySpan<byte> text = bytes;
        if (text.StartsWith(Encoding.UTF8.Preamble))
        {
            text = text[Encoding.UTF8.Preamble.Length..];
        }
        try
        {
            return StrictUtf8.GetString(text);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // Each source and its .uid, each existing output and output .uid the move replaces, and each file to be edited, the
    // last from the bytes its edit was made from.
    private static bool BackUp(ProjectRun run, List<Planned> planned, List<ScriptEdit> edits)
    {
        List<string> files = [];
        foreach (Planned item in planned)
        {
            files.Add(item.Source.File.Path);
            files.AddRange(new[] { item.Source.File.Path + ".uid", item.Output, item.Output + ".uid" }.Where(File.Exists));
        }
        List<(string Path, byte[] Bytes)> read = [.. edits.Select(edit => (edit.Path, edit.Original))];
        return run.Staging.TryBackUp(files, read);
    }

    private static bool MoveIn(ProjectRun run, List<Planned> planned)
    {
        foreach (Planned item in planned)
        {
            if (!MoveSidecar(item, run.Invocation.Error) || !ProjectStaging.TryMove(item.Staged!, item.Output, run.Invocation.Error))
            {
                return false;
            }
            run.Invocation.Output.WriteLine($"{item.Source.File.Path} -> {item.Output}");
            run.Tally.Converted++;
        }
        return true;
    }

    // A conversion with no uid staged no sidecar, so one left beside the output from an earlier conversion goes, as in
    // a plain run.
    private static bool MoveSidecar(Planned item, TextWriter error)
    {
        if (!item.Source.File.Kind.FromTscn)
        {
            return true;
        }
        string staged = item.Staged! + ".uid";
        string target = item.Output + ".uid";
        if (File.Exists(staged))
        {
            return ProjectStaging.TryMove(staged, target, error);
        }
        return !File.Exists(target) || InputFiles.TryDelete(target, error);
    }

    private static bool WriteEdits(ProjectRun run, List<ScriptEdit> edits)
    {
        foreach (ScriptEdit edit in edits)
        {
            if (!InputFiles.TryWrite(edit.Path, edit.Bytes, run.Invocation.Error))
            {
                return false;
            }
            run.Invocation.Output.WriteLine($"{edit.Path}: {edit.Rewritten} path(s) rewritten");
        }
        return true;
    }

    private static bool RemoveOriginals(ProjectRun run, List<Planned> planned)
    {
        foreach (Planned item in planned)
        {
            string source = item.Source.File.Path;
            bool removed =
                InputFiles.TryDelete(source, run.Invocation.Error)
                && (!File.Exists(source + ".uid") || InputFiles.TryDelete(source + ".uid", run.Invocation.Error));
            if (!removed)
            {
                return false;
            }
        }
        run.Invocation.Output.WriteLine($"{planned.Count} original(s) removed");
        return true;
    }

    private static void WriteError(Tally tally)
    {
        tally.Errors++;
        tally.WriteErrors++;
    }

    // Where Place writes the output: beside its source, or in the staging folder, whose subfolder is made first; null
    // when that subfolder cannot be made.
    private static string? WriteTarget(Planned item, TextWriter error)
    {
        if (item.Staged is null)
        {
            return item.Output;
        }
        return TryCreateFolder(Path.GetDirectoryName(item.Staged)!, error) ? item.Staged : null;
    }

    private static bool TryCreateFolder(string folder, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (IOException)
        {
            error.WriteLine($"{folder}: cannot be written.");
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{folder}: access denied.");
        }
        return false;
    }

    // A pair clash or an existing output refuses a source before any conversion runs; a document the core refuses
    // drops it with its own error. Only the sources left are converted, and only they enter the rename map.
    private static List<Planned> Plan(List<Source> sources, HashSet<string> inputs, MetadataChoice choice, Tally tally, TextWriter output)
    {
        var outputCounts = new Dictionary<string, int>(InputFiles.PathComparer);
        foreach (Source source in sources)
        {
            string full = Path.GetFullPath(OutputOf(source));
            outputCounts[full] = outputCounts.GetValueOrDefault(full) + 1;
        }
        List<(Source Source, string Output)> candidates = [];
        foreach (Source source in sources)
        {
            string outputPath = OutputOf(source);
            string? refusal = Refusal(source, outputPath, inputs, outputCounts[Path.GetFullPath(outputPath)] > 1, tally);
            if (refusal is not null)
            {
                output.WriteLine($"{source.File.Path}: {refusal}");
                tally.Refused++;
                continue;
            }
            candidates.Add((source, outputPath));
        }
        // The check pass reads the candidates' sources for their outputs; Convert narrows this to the planned items.
        tally.SceneFiles.Converting(candidates.Select(candidate => (candidate.Source.File.Path, candidate.Output)));
        List<Planned> planned = [];
        foreach ((Source source, string outputPath) in candidates)
        {
            Planned? item = Prepare(source, outputPath, choice, tally, output);
            if (item is not null)
            {
                planned.Add(item);
            }
        }
        return planned;
    }

    // Converts one source to its document and the text it writes, which the run keeps; null when the core refused it or
    // reported an error. A .tscn or .tres has no marker, so it takes the unmarked choice; a .xscn or .xres its own.
    private static Planned? Prepare(Source source, string outputPath, MetadataChoice choice, Tally tally, TextWriter output)
    {
        (GodotMetadata Metadata, int Warnings) chosen = source.File.Kind.FromTscn
            ? choice.Unmarked(source.File.Path)
            : choice.Of(source.File.Path, source.Text);
        tally.Warnings += chosen.Warnings;
        GodotMetadata metadata = chosen.Metadata;
        var options = new ConversionOptions { Seed = SeedOf(SeedKeyOf(source.File.Path, outputPath)), Uid = source.Uid };
        (XscnDocument? Document, string? Uid) prepared;
        try
        {
            prepared = source.File.Kind.FromTscn ? FromTscn(source, metadata, options, tally, output) : FromXscn(source, metadata, tally, output);
        }
        catch (Exception exception) when (WriterRefusal.Is(exception))
        {
            output.WriteLine(CheckCommand.CoreRefusal(source.File.Path, exception));
            tally.Errors++;
            return null;
        }
        if (prepared.Document is null)
        {
            return null;
        }
        var item = new Planned(source, outputPath, prepared.Document, prepared.Uid, metadata);
        // The text is produced once with the unrewritten document, so a document the writer refuses or a conversion
        // that reports an error drops the source before the rename map is built from it. It is kept: a document the
        // rewrite leaves as it was is not produced again.
        item.Checked = Text(item, tally, output, check: true);
        return item.Checked is null ? null : item;
    }

    // The rename map of each project root, from every source the run converts under it to its output.
    private static Dictionary<string, Dictionary<string, string>> Renames(List<Planned> planned, Tally tally, TextWriter output)
    {
        var roots = new Dictionary<string, Dictionary<string, string>>(InputFiles.PathComparer);
        var listings = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Planned item in planned)
        {
            string? root = GodotProject.Find(item.Source.File.Path);
            item.Root = root;
            if (root is null)
            {
                Warn(output, tally, item.Source.File.Path, CliCodes.NoProjectRoot, NoRootMessage, 1);
                continue;
            }
            if (!roots.TryGetValue(root, out Dictionary<string, string>? map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                roots.Add(root, map);
            }
            map[GodotProject.ResPath(root, item.Source.File.Path, listings)] = GodotProject.ResPath(root, item.Output, listings);
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
            item.Checked = result.Changed ? null : item.Checked;
            foreach (string warning in result.Warnings)
            {
                Warn(output, tally, item.Source.File.Path, CliCodes.UnrewrittenPath, warning, 1);
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
    // counts as an error of the run, which goes on with the rest. A staged output is written to the staging folder, and
    // counts as converted only once it is moved into place.
    private static void Place(Planned item, Tally tally, InvocationConfiguration invocation)
    {
        Converted? converted = Produce(item, tally, invocation);
        if (converted is null)
        {
            return;
        }
        string? target = WriteTarget(item, invocation.Error);
        if (target is null)
        {
            WriteFailed(item, tally);
            return;
        }
        string? sidecar = item.Source.File.Kind.FromTscn ? target + ".uid" : null;
        byte[]? sidecarBefore = null;
        if (sidecar is not null && !TryPlaceSidecar(item, sidecar, converted.SidecarUid, invocation.Error, out sidecarBefore))
        {
            WriteFailed(item, tally);
            return;
        }
        if (!TryWriteOutput(item, target, converted.Text, invocation.Error))
        {
            // The sidecar belongs to the output that was not written, so it goes back to what it was.
            RestoreSidecar(sidecar, sidecarBefore, invocation.Error);
            WriteFailed(item, tally);
            return;
        }
        if (item.Staged is null)
        {
            invocation.Output.WriteLine($"{item.Source.File.Path} -> {item.Output}");
            tally.Converted++;
        }
        item.Written = true;
        item.WrittenText = converted.Text;
    }

    // The text to write for a source: the plan's kept text, whose issues the run reports now, or a fresh conversion.
    private static Converted? Produce(Planned item, Tally tally, InvocationConfiguration invocation)
    {
        if (item.Checked is { } kept)
        {
            Report(item.Source.File.Path, kept.Issues, tally, invocation.Output, errorsOnly: false);
            return kept;
        }
        return Text(item, tally, invocation.Output, check: false);
    }

    private static bool TryWriteOutput(Planned item, string target, string text, TextWriter error) =>
        item.Staged is null ? InputFiles.TryWrite(target, text, error) : InputFiles.TryWriteStaged(target, text, error);

    private static void WriteFailed(Planned item, Tally tally)
    {
        tally.Errors++;
        tally.WriteErrors++;
        item.WriteFailed = true;
    }

    // The text to write; null when the core refused the document or a conversion issue is an error, so nothing is
    // written. A check reports only errors, since the run reports every issue when it writes the file.
    private static Converted? Text(Planned item, Tally tally, TextWriter output, bool check)
    {
        try
        {
            if (item.Source.File.Kind.FromTscn)
            {
                return new Converted(XscnWriter.Write(item.Document, item.Metadata), item.Uid, []);
            }
            var options = new ConversionOptions
            {
                Seed = SeedOf(SeedKeyOf(item.Source.File.Path, item.Output)),
                Uid = item.Uid,
                ScriptClassOf = tally.ScriptClassOf(item.Source.File.Path),
                SceneFiles = tally.SceneFiles,
                SourceKey = Path.GetFullPath(item.Source.File.Path),
            };
            ConversionResult<TscnDocument> result = XscnToTscn.Convert(item.Document, item.Metadata, options);
            return Report(item.Source.File.Path, result.Issues, tally, output, errorsOnly: check)
                ? new Converted(TscnWriter.Write(result.Document), SidecarUid: null, result.Issues)
                : null;
        }
        catch (Exception exception) when (WriterRefusal.Is(exception))
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
            Warn(output, tally, item.Source.File.Path, CliCodes.SharedUid, message, 1);
        }
    }

    // A written file that refers to the output of a file this run could not write: the reference would break, and the
    // old file is still there.
    private static void ReportFailedWrites(List<Planned> planned, Tally tally, TextWriter output)
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
                string failedPath = GodotProject.ResPath(root, failed.Output);
                if (!RefersTo(written.Document, failedPath, written.Metadata))
                {
                    continue;
                }
                string oldPath = GodotProject.ResPath(root, failed.Source.File.Path);
                string message = $"refers to {failedPath}, which this run failed to write; {oldPath} still exists";
                (int Line, int Column)? at = FirstOccurrence(written.WrittenText, ReferenceScan.GrammarOf(written.Output), failedPath);
                string path = at is { } location ? Location(written.Output, location.Line, location.Column) : written.Output;
                Warn(output, tally, path, CliCodes.FailedWrite, message, 1);
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
    // A file in alsoSkip was already reported and is not read again.
    private static void Scan(List<Planned> planned, Tally tally, InvocationConfiguration invocation, IEnumerable<string> alsoSkip)
    {
        var skip = new HashSet<string>(alsoSkip.Select(Path.GetFullPath), InputFiles.PathComparer);
        var roots = new Dictionary<string, Dictionary<string, string>>(InputFiles.PathComparer);
        // The disk has changed since the plan's listings, so the scan lists again.
        var listings = new Dictionary<string, string[]>(StringComparer.Ordinal);
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
            map[GodotProject.ResPath(root, item.Source.File.Path, listings)] = GodotProject.ResPath(root, item.Output, listings);
        }
        foreach ((string root, Dictionary<string, string> map) in roots)
        {
            ScanRoot(root, map, skip, tally, invocation);
        }
    }

    private static void ScanRoot(string root, Dictionary<string, string> map, HashSet<string> skip, Tally tally, InvocationConfiguration invocation)
    {
        // The addon's own files name the extensions it converts on purpose, so a project run leaves that one folder out of
        // the scan; every other folder under addons/ is still scanned.
        string? excluded = tally.Project ? Path.GetFullPath(Path.Combine(root, "addons", "godot_xaml")) : null;
        List<ScannedReference> references = [.. ReferenceScan.Find(root, map, skip, excluded, invocation.Error)];
        foreach (IGrouping<string, ScannedReference> file in references.GroupBy(reference => reference.File, InputFiles.PathComparer))
        {
            ScanFile(root, file, tally, invocation);
        }
    }

    // One file's hits, in the order the scan found them. A whole reference is a line of its own; a built path prints
    // while the file has three or fewer, and the fourth stands for the rest of that file.
    private static void ScanFile(string root, IGrouping<string, ScannedReference> file, Tally tally, InvocationConfiguration invocation)
    {
        bool underAddons = tally.Project && IsUnderAddons(root, file.Key);
        int built = file.Count(reference => reference.Kind != ScriptHitKind.Whole);
        int printed = 0;
        foreach (ScannedReference reference in file)
        {
            string path = Location(reference.File, reference.Line, reference.Column);
            if (reference.Kind == ScriptHitKind.Whole)
            {
                Warn(invocation.Output, tally, path, CliCodes.UntouchedReference, ReferenceMessage(reference, underAddons), 1);
                continue;
            }
            printed++;
            if (BuiltPathMessage(reference, printed, built, underAddons) is { } line)
            {
                Warn(invocation.Output, tally, path, CliCodes.BuiltPath, line.Message, line.Hits);
            }
        }
    }

    // The message a built path prints and the number of hits it stands for, or null when an earlier hit of the same file
    // already rolled the rest up.
    private static (string Message, int Hits)? BuiltPathMessage(ScannedReference reference, int printed, int built, bool underAddons)
    {
        if (printed > RolledUpAt)
        {
            return null;
        }
        if (printed == RolledUpAt)
        {
            int hidden = built - (RolledUpAt - 1);
            return ($"and {hidden} more like it in this file; none is rewritten", hidden);
        }
        return (ReferenceMessage(reference, underAddons), 1);
    }

    // Whether a file is under the root's own addons folder, the first segment below the root, which the run never edits.
    private static bool IsUnderAddons(string root, string file)
    {
        string[] segments = Path.GetRelativePath(root, Path.GetFullPath(file))
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && InputFiles.PathComparer.Equals(segments[0], "addons");
    }

    private static string ReferenceMessage(ScannedReference reference, bool underAddons)
    {
        if (reference.Kind == ScriptHitKind.Inside)
        {
            return underAddons
                ? $"holds {reference.Key}, which this run converts to {reference.Value}, inside a longer string; "
                    + "addons/ is not edited, so it is not rewritten"
                : $"holds {reference.Key}, which this run converts to {reference.Value}, inside a longer string; it is not rewritten";
        }
        if (reference.Kind == ScriptHitKind.Fragment)
        {
            return underAddons
                ? $"ends in {reference.Key}; this run converts such files to {reference.Value} but edits nothing under addons/, "
                    + "so a path built from it is not rewritten"
                : $"ends in {reference.Key}, and this run converts such files to {reference.Value}; a path built from it is not rewritten";
        }
        string clause = reference.CarriesUid
            ? "still resolves by its uid once the source is removed and the uid moves with the file"
            : "no uid on this reference; it breaks once the source is removed";
        return $"refers to {reference.Key}, which this run converts to {reference.Value}; {clause}";
    }

    private static void Warn(TextWriter output, Tally tally, string path, string code, string message, int hits)
    {
        output.WriteLine($"{path}: warning {code}: {message}");
        tally.Warnings += hits;
    }

    private static string Location(string path, int line, int column) => $"{path}({line},{column})";

    // A conversion without a uid removes a sidecar left from an earlier one, which would give the output a uid it lacks.
    // The sidecar's bytes before the step are kept, null when there was none, so the step can be undone.
    private static bool TryPlaceSidecar(Planned item, string sidecar, string? uid, TextWriter error, out byte[]? before)
    {
        before = null;
        if (File.Exists(sidecar) && !TryReadBytes(sidecar, error, out before))
        {
            return false;
        }
        if (uid is not null)
        {
            return TryWriteOutput(item, sidecar, uid + "\n", error);
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

    private static string OutputOf(Source source) => Path.ChangeExtension(source.File.Path, source.File.Kind.OutputExtension);

    // Why a source is not converted, or null. Converting both files of a pair in one run would swap them, and two
    // sources with one output would write over each other, so none of them is converted.
    private static string? Refusal(Source source, string outputPath, HashSet<string> inputs, bool sharedOutput, Tally tally)
    {
        if (inputs.Contains(Path.GetFullPath(outputPath)))
        {
            return $"{outputPath} is also being converted";
        }
        return sharedOutput ? $"another source also converts to {outputPath}" : DiskRefusal(source, outputPath, tally);
    }

    // An existing output is refused without --force. A project run also refuses, even with --force, a folder where the
    // output or its sidecar goes and a read-only or unresolvable file among those it replaces or removes, which it would
    // otherwise find only after outputs had moved in; a plain run reports those as write errors.
    private static string? DiskRefusal(Source source, string outputPath, Tally tally)
    {
        string? sidecar = source.File.Kind.FromTscn ? outputPath + ".uid" : null;
        string? existing = FirstExisting(outputPath, sidecar);
        if (existing is not null && !tally.Overwrite)
        {
            return $"{existing} exists (use --force)";
        }
        return tally.Project ? ProjectDiskRefusal(source.File.Path, outputPath, sidecar) : null;
    }

    private static string? ProjectDiskRefusal(string sourcePath, string outputPath, string? sidecar)
    {
        string? folder = new[] { outputPath, sidecar }.FirstOrDefault(Directory.Exists);
        if (folder is not null)
        {
            return $"{folder} is a folder";
        }
        return new[] { sourcePath, sourcePath + ".uid", outputPath, sidecar }.Select(EntryRefusal).FirstOrDefault(refusal => refusal is not null);
    }

    // Why a file the move or the delete acts on refuses its source, or null. Both act on a link itself, so a link is
    // judged by its own attribute; one that cannot be followed to its final target, a loop among them, is refused too.
    private static string? EntryRefusal(string? path)
    {
        if (path is null)
        {
            return null;
        }
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                info.ResolveLinkTarget(returnFinalTarget: true);
            }
            return IsReadOnlyEntry(path) ? $"{path} is read-only" : null;
        }
        catch (IOException)
        {
            return $"{path} cannot be resolved";
        }
    }

    private static bool IsReadOnlyEntry(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Attributes.HasFlag(FileAttributes.ReadOnly);
    }

    // An edit writes through a link, and a link's own attribute says nothing of the file a write lands in, so a link is
    // judged by its final target.
    private static bool IsReadOnlyTarget(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return false;
        }
        FileSystemInfo target = info.LinkTarget is null ? info : info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
        return target.Exists && target.Attributes.HasFlag(FileAttributes.ReadOnly);
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
            output.WriteLine($"{source.File.Path}({exception.Line},{exception.Column}): error {CliCodes.ReadFailed}: {exception.Reason}");
            tally.Errors++;
            return (null, null);
        }
        XscnDocumentKind held = document.Kind == TscnKind.Scene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource;
        if (held != source.File.Kind.Kind)
        {
            output.WriteLine($"{source.File.Path}: error {CliCodes.KindMismatch}: {KindMismatch(held)}");
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
    private sealed class Planned(Source source, string output, XscnDocument document, string? uid, GodotMetadata metadata)
    {
        public Source Source { get; } = source;

        public string Output { get; } = output;

        // The metadata the whole item is planned, converted and written with, chosen from its source's marker.
        public GodotMetadata Metadata { get; } = metadata;

        public XscnDocument Document { get; set; } = document;

        public string? Uid { get; } = uid;

        public string? Root { get; set; }

        // The text the plan's check produced, while the rewrite has not changed the document.
        public Converted? Checked { get; set; }

        public bool Written { get; set; }

        public string WrittenText { get; set; } = "";

        public bool WriteFailed { get; set; }

        // Where a project run writes the output before moving it into place; null in a plain run.
        public string? Staged { get; set; }
    }

    private sealed record ConvertOptions(
        Argument<string[]> Paths,
        Option<string> Metadata,
        Option<bool> Force,
        Option<string> Project,
        Option<bool> Back,
        Option<bool> NoBackup
    );

    // A --project run's root, as a full path with no trailing separator, and whether it keeps a backup.
    private sealed record ProjectSettings(string Root, bool NoBackup, bool Back);

    // A file a project run edits: its bytes after the edit, the bytes the edit was made from, and how many literals changed.
    private sealed record ScriptEdit(string Path, byte[] Bytes, byte[] Original, int Rewritten);

    // A project run's settings, counts and streams, and the folders it made.
    private sealed class ProjectRun(ProjectSettings settings, Tally tally, InvocationConfiguration invocation)
    {
        public string Root => settings.Root;

        public bool NoBackup => settings.NoBackup;

        public bool Back => settings.Back;

        // The scripts warned as not UTF-8, which the reference scan then leaves alone.
        public List<string> NotUtf8 { get; } = [];

        public Tally Tally { get; } = tally;

        public InvocationConfiguration Invocation { get; } = invocation;

        // The staging folder, the backup and the moves the run's outputs go through.
        public ProjectStaging Staging { get; } = new(settings.Root, settings.NoBackup, invocation.Output, invocation.Error);

        public string? Backup => Staging.Backup;
    }

    // The text to write, for a .xscn or .xres the uid its .uid sidecar holds (null for none), and the conversion issues the
    // run reports when it writes the text.
    private sealed record Converted(string Text, string? SidecarUid, ImmutableArray<ConversionIssue> Issues);

    // The run's overwrite setting and its counts.
    private sealed class Tally
    {
        public bool Overwrite { get; init; }

        public bool Project { get; init; }

        // The scenes the way back reads for the class of an instanced node, shared by every conversion of the run.
        public required RunSceneFiles SceneFiles { get; init; }

        // The global class lookup of each project root the run reads the class cache of; a root with no usable cache is
        // cached as null too, so its file is read once.
        private readonly Dictionary<string, Func<string, string?>?> scriptClasses = new(InputFiles.PathComparer);

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

        // The global class of the script at a res:// path for the project the source belongs to, or null when no project
        // holds it or its cache gives no answer for the script: the core's GXC0106 then reports the missing lookup.
        public Func<string, string?>? ScriptClassOf(string sourcePath)
        {
            string? root = GodotProject.Find(Path.GetFullPath(sourcePath));
            if (root is null)
            {
                return null;
            }
            if (!scriptClasses.TryGetValue(root, out Func<string, string?>? lookup))
            {
                lookup = GlobalClassCache.Read(root);
                scriptClasses[root] = lookup;
            }
            return lookup;
        }
    }
}
