using System.CommandLine;
using System.CommandLine.Invocation;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli;

/// <summary>
/// The <c>upgrade</c> command: reads every <c>.xscn</c> and <c>.xres</c> of a project with the metadata of the Godot version it
/// was written with (its <c>x:Godot</c> marker, else <c>--from</c>) and writes it with <c>--to</c>'s, stamping the new
/// marker. All or nothing: an error in any file changes nothing; otherwise every changed file is staged, the originals
/// backed up and the outputs moved in, as <c>convert --project</c> does. With <c>--check</c> it names each file that would
/// change and writes nothing.
/// </summary>
internal static class UpgradeCommand
{
    private static readonly AcceptedFiles<XscnDocumentKind> Accepted = new(InputFiles.XscnExtensions, "only .xscn and .xres files can be upgraded.");

    private static readonly string[] Extensions = [".xscn", ".xres"];

    /// <summary>The command, its project argument and its options.</summary>
    /// <param name="versions">The Godot versions this run carries, and how it loads one's metadata.</param>
    /// <returns>The command.</returns>
    internal static Command Create(MetadataVersions versions)
    {
        var options = new UpgradeOptions(
            new Argument<string>("project") { Description = "A Godot project's root folder, the one holding project.godot." },
            new Option<string>("--to")
            {
                Description = "The Godot version whose metadata every file is written with.",
                HelpName = "version",
                Required = true,
            },
            new Option<string>("--from")
            {
                Description = "The Godot version a file with no x:Godot marker was written with; a marked file must agree with it.",
                HelpName = "version",
            },
            new Option<bool>("--check") { Description = "Changes nothing; names each file that would change, and exits 1 when any would." }
        );
        var command = new Command("upgrade", "Moves a project's .xscn and .xres files to another Godot version's metadata, all or nothing.");
        command.Arguments.Add(options.Project);
        command.Options.Add(options.To);
        command.Options.Add(options.From);
        command.Options.Add(options.Check);
        command.SetAction(parseResult => Run(parseResult, options, versions));
        return command;
    }

    // Exit 2 is everything that stops the run before a file is judged: a version the run does not carry, a folder with no
    // project.godot, and a folder or file that cannot be read.
    private static int Run(ParseResult parseResult, UpgradeOptions options, MetadataVersions versions)
    {
        InvocationConfiguration invocation = parseResult.InvocationConfiguration;
        string to = parseResult.GetValue(options.To)!;
        string? from = parseResult.GetValue(options.From);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parseResult.GetValue(options.Project)!));
        string? usage = UsageError(to, from, versions) ?? ProjectError(root);
        if (usage is not null)
        {
            invocation.Error.WriteLine(usage);
            return 2;
        }
        List<(InputFile<XscnDocumentKind> File, string Text)>? read = ReadProject(root, invocation.Error);
        if (read is null)
        {
            return 2;
        }
        var run = new UpgradeRun(versions, from, to, invocation.Output) { Error = invocation.Error };
        List<(string Path, string Text)> changed = Changed(run, read);
        return parseResult.GetValue(options.Check) ? Check(run, changed, read.Count) : Apply(run, root, changed, read.Count);
    }

    private static string? UsageError(string to, string? from, MetadataVersions versions) =>
        VersionError("--to", to, versions) ?? (from is null ? null : VersionError("--from", from, versions));

    // Each file whose upgraded text differs from its own; a file with an error is counted by the run and left out.
    private static List<(string Path, string Text)> Changed(UpgradeRun run, List<(InputFile<XscnDocumentKind> File, string Text)> read)
    {
        List<(string Path, string Text)> changed = [];
        foreach ((InputFile<XscnDocumentKind> file, string text) in read)
        {
            if (run.Upgrade(file, text) is { } upgraded && !string.Equals(upgraded, text, StringComparison.Ordinal))
            {
                changed.Add((file.Path, upgraded));
            }
        }
        return changed;
    }

    private static string? VersionError(string option, string version, MetadataVersions versions) =>
        versions.Carried.Contains(version, StringComparer.Ordinal)
            ? null
            : $"{option} {version}: this tool carries no metadata for Godot {version}; it carries {string.Join(", ", versions.Carried)}";

    private static string? ProjectError(string root) =>
        File.Exists(Path.Combine(root, "project.godot")) ? null : $"{root}: no project.godot in this folder.";

    // Every scene and resource under the root, by the walk convert --project makes, addons/ left out; each read before
    // any is judged.
    private static List<(InputFile<XscnDocumentKind> File, string Text)>? ReadProject(string root, TextWriter error)
    {
        List<string>? found = ReferenceScan.ProjectFiles(root, Extensions, skipAddons: true, error);
        if (found is null)
        {
            return null;
        }
        List<InputFile<XscnDocumentKind>> files = [];
        foreach (string path in found)
        {
            if (Accepted.TryGetKind(path, out XscnDocumentKind kind))
            {
                files.Add(new InputFile<XscnDocumentKind>(path, kind));
            }
        }
        return InputFiles.ReadAll(files, error);
    }

    private static int Check(UpgradeRun run, List<(string Path, string Text)> changed, int total)
    {
        foreach ((string path, _) in changed)
        {
            run.Output.WriteLine($"{path}: would upgrade");
        }
        run.Output.WriteLine($"{changed.Count} of {total} file(s) would upgrade, {run.Warnings} warning(s)");
        return run.Errors > 0 || changed.Count > 0 ? 1 : 0;
    }

    // All or nothing: with an error in any file nothing is written. Otherwise each changed file is staged, the originals
    // backed up, and each staged file moved over its original; the staging folder never outlives the run. A failure from
    // the first move on stops there and says where the backup is; nothing is rolled back.
    private static int Apply(UpgradeRun run, string root, List<(string Path, string Text)> changed, int total)
    {
        if (run.Errors > 0 || changed.Count == 0)
        {
            run.Output.WriteLine($"0 of {total} file(s) upgraded, {run.Warnings} warning(s)");
            return run.Errors > 0 ? 1 : 0;
        }
        using var staging = new ProjectStaging(root, noBackup: false, run.Output, run.Error);
        List<string>? staged = staging.TryStage(changed);
        if (staged is null || !staging.TryBackUp([.. changed.Select(file => file.Path)], []))
        {
            return 2;
        }
        for (int i = 0; i < changed.Count; i++)
        {
            if (!ProjectStaging.TryMove(staged[i], changed[i].Path, run.Error))
            {
                run.Error.WriteLine($"{root}: the project is part upgraded; restore it from {staging.Backup}");
                return 2;
            }
            run.Output.WriteLine($"{changed[i].Path}: upgraded");
        }
        run.Output.WriteLine($"{changed.Count} of {total} file(s) upgraded, {run.Warnings} warning(s)");
        return 0;
    }

    private sealed record UpgradeOptions(Argument<string> Project, Option<string> To, Option<string> From, Option<bool> Check);

    // One run's versions, its counts and its writers; each file is read with its own version and written with --to's.
    private sealed class UpgradeRun(MetadataVersions versions, string? from, string to, TextWriter output)
    {
        private readonly Dictionary<string, GodotMetadata> loaded = new(StringComparer.Ordinal);

        public TextWriter Output => output;

        public TextWriter Error { get; init; } = TextWriter.Null;

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        // The file's text written with --to's metadata, or null when it has an error, every problem printed.
        public string? Upgrade(InputFile<XscnDocumentKind> file, string text)
        {
            string? version = VersionOf(file.Path, text);
            if (version is null)
            {
                Errors++;
                return null;
            }
            XscnParseResult result = XscnParser.Parse(text, file.Kind, Metadata(version));
            LineMap map = new(text);
            foreach (XscnDiagnostic diagnostic in result.Diagnostics)
            {
                output.WriteLine(CheckCommand.Format(file.Path, map, diagnostic));
                Count(diagnostic.Severity == XscnSeverity.Error);
            }
            if (result.HasErrors)
            {
                return null;
            }
            WarnRemoved(file.Path, result.Document, Metadata(version));
            return Write(file.Path, result.Document);
        }

        // The version the file is read with: its marker, which must be carried and agree with --from, else --from.
        private string? VersionOf(string path, string text)
        {
            string? marker = XscnMarker.Read(text);
            string? message = marker switch
            {
                null when from is null => $"{CliCodes.NoMarker}: no x:Godot marker; pass --from with the version it was written with",
                null => null,
                _ when !versions.Carried.Contains(marker, StringComparer.Ordinal) =>
                    $"{CliCodes.MarkerNotCarried}: written with Godot {marker}'s metadata, which this tool does not carry; not upgraded",
                _ when from is not null && from != marker => $"{CliCodes.MarkerDisagrees}: x:Godot is {marker} but --from is {from}",
                _ => null,
            };
            if (message is not null)
            {
                output.WriteLine($"{path}: error {message}");
                return null;
            }
            return marker ?? from;
        }

        // Each property of a known class that the file's version has and --to's no longer does, once per class and property.
        // The writer keeps it as a script property, an attribute under its Godot name holding its Godot value text, since
        // --to's metadata no longer types it.
        private void WarnRemoved(string path, XscnDocument document, GodotMetadata old)
        {
            GodotMetadata next = Metadata(to);
            var warned = new HashSet<(string, string)>();
            foreach ((GodotClassInfo cls, XscnProperty property) in ClassProperties(document))
            {
                bool removed =
                    old.FindProperty(cls, property.GodotName) is not null
                    && next.FindClass(cls.Name) is { } kept
                    && next.FindProperty(kept, property.GodotName) is null;
                if (removed && warned.Add((cls.Name, property.GodotName)))
                {
                    string message =
                        $"{cls.Name}.{property.GodotName} is not in Godot {to}'s metadata; kept as a script property with its value text";
                    output.WriteLine($"{path}: warning {CliCodes.PropertyRemoved}: {message}");
                    Warnings++;
                }
            }
        }

        private string? Write(string path, XscnDocument document)
        {
            try
            {
                return XscnWriter.Write(document, Metadata(to));
            }
            catch (Exception exception) when (WriterRefusal.Is(exception))
            {
                output.WriteLine(CheckCommand.CoreRefusal(path, exception));
                Errors++;
                return null;
            }
        }

        private void Count(bool isError)
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

        // Each carried version's metadata is loaded once per run.
        private GodotMetadata Metadata(string version)
        {
            if (!loaded.TryGetValue(version, out GodotMetadata? metadata))
            {
                metadata = versions.Load(version);
                loaded.Add(version, metadata);
            }
            return metadata;
        }
    }

    // Every property set on an element of a class the metadata knows: nodes, overrides' children, table resources and
    // inline resources, in no promised order.
    private static IEnumerable<(GodotClassInfo Class, XscnProperty Property)> ClassProperties(XscnDocument document)
    {
        var pending = new Stack<XscnElement>([document.Root, .. document.Resources]);
        while (pending.Count > 0)
        {
            XscnElement element = pending.Pop();
            (XscnType? type, IEnumerable<XscnProperty> properties, IEnumerable<XscnElement> children) = element switch
            {
                XscnNode node => (node.Type, node.Properties, node.Children),
                XscnNodeElement other => ((XscnType?)null, other.Properties, other.Children),
                XscnResource resource => (resource.Type, resource.Properties, []),
                _ => (null, [], []),
            };
            foreach (XscnElement child in children.Concat(properties.Select(p => p.Value).OfType<XscnInline>().Select(i => i.Resource)))
            {
                pending.Push(child);
            }
            if (type is XscnClassType known)
            {
                foreach (XscnProperty property in properties)
                {
                    yield return (known.Class, property);
                }
            }
        }
    }
}
