// Explicit, so the copy compare-project.ps1 installs builds in a project that does not enable nullable references.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using GodotArray = Godot.Collections.Array;
using GodotDictionary = Godot.Collections.Dictionary;

namespace GodotXaml.Addon.Compare;

/// <summary>
/// Loads each fixture in <c>res://cases/</c> with its converted counterpart and compares a canonical dump of the two: a
/// scene's packed state and node ids, then the tree it instantiates, or a resource and its stored properties. Prints
/// <c>COMPARE &lt;name&gt; PASS</c> or <c>COMPARE &lt;name&gt; FAIL &lt;detail&gt;</c> per fixture, then
/// <c>DONE &lt;passed&gt;/&lt;total&gt;</c>, and quits with exit code 0 only when every fixture passed. The expected pair
/// count is the <c>--expected-pairs=&lt;n&gt;</c> user argument, which <c>godot/compare.ps1</c> passes from the number of
/// fixture files it copied.
/// <para>
/// With the user argument <c>--dump-tree=&lt;absolute folder&gt;</c> it dumps a whole project instead, for
/// <c>godot/compare-project.ps1</c>: every <c>.tscn</c>, <c>.xscn</c>, <c>.tres</c> and <c>.xres</c> under <c>res://</c>
/// (but <c>res://addons</c>, dot folders, folders holding a <c>.gdignore</c> or their own <c>project.godot</c>,
/// <c>bin</c> and <c>obj</c> beside a <c>.csproj</c>, links and this scene's own folder) is written to
/// <c>&lt;folder&gt;/&lt;relative path without extension&gt;.scene.dump</c> or <c>.resource.dump</c>, with a
/// <c>DUMP &lt;relative path&gt; OK</c> or <c>DUMP &lt;relative path&gt; FAIL &lt;reason&gt;</c> line per file, then
/// <c>DUMPED &lt;ok&gt;/&lt;total&gt;</c>. A second file with the same dump path fails as
/// <c>collides with &lt;first relative path&gt;</c>. It quits with exit code 0 only when at least one file was found
/// and every file dumped.
/// </para>
/// </summary>
/// <remarks>
/// The dump normalises exactly three things: a path's <c>.tscn</c>, <c>.xscn</c>, <c>.tres</c> or <c>.xres</c> extension
/// is removed, a built-in sub-resource's id after <c>::</c> is printed as <c>*</c>, since <c>.xscn</c> stores no
/// sub-resource ids and the conversion mints new ones, and a script's source text has the conversion's path rewrites
/// undone, since <c>convert --project</c> rewrites exact-path literals inside scripts. A sub-resource's identity is still
/// checked by the <c>res(seen:&lt;n&gt;)</c> ordinals and its <c>subprop</c> lines.
/// </remarks>
public partial class Compare : Node
{
    private const string CasesPath = "res://cases/";
    private const string ResRoot = "res://";
    private const string DumpPrefix = "--dump-tree=";

    // The files the dump mode dumps.
    private static readonly string[] DumpedExtensions = [".tscn", ".xscn", ".tres", ".xres"];

    // Each pair kind's source and golden extension, paired as the two sides of one dump.
    private static readonly (PairKind Kind, string Source, string Converted)[] Kinds =
    [
        (PairKind.Scene, ".tscn", ".xscn"),
        (PairKind.Resource, ".tres", ".xres"),
    ];

    private enum PairKind
    {
        Scene,
        Resource,
    }

    // One fixture: its stem, its kind, and the extension of each side's file.
    private sealed record Pair(string Name, PairKind Kind, string Source, string Converted);

    /// <inheritdoc/>
    public override void _Ready()
    {
        if (DumpArgument() is string dumpArgument)
        {
            RunDump(dumpArgument);
            return;
        }

        int expected = 0;
        int passed = 0;
        try
        {
            // Read inside the try, so a failure here still reaches the finally that prints DONE and quits.
            expected = ExpectedPairs();
            if (expected == 0)
            {
                GD.Print("COMPARE setup FAIL no --expected-pairs=<n> argument");
                return;
            }

            Pair[] pairs = PairNames();
            if (pairs.Length != expected)
            {
                GD.Print($"COMPARE setup FAIL expected {expected} pairs, found {pairs.Length}");
                return;
            }

            foreach (Pair pair in pairs)
            {
                passed += Report(pair) ? 1 : 0;
            }
        }
        finally
        {
            GD.Print($"DONE {passed}/{expected}");

            // A Variant the dump left to its finalizer, freed once the engine has shut down, crashes the process at exit.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GetTree().Quit(passed == expected && expected > 0 ? 0 : 1);
        }
    }

    // The whole --dump-tree=<folder> user argument, or null when the run is the pair mode.
    private static string? DumpArgument() =>
        OS.GetCmdlineUserArgs().FirstOrDefault(argument => argument.StartsWith(DumpPrefix, StringComparison.Ordinal));

    // The dump mode: every scene and resource file of the project, dumped under the argument's folder.
    private void RunDump(string argument)
    {
        bool ready = false;
        int dumped = 0;
        int total = 0;
        try
        {
            string? folder = DumpFolder(argument);
            if (folder is null)
            {
                return;
            }

            string[] files = [.. ProjectFiles(ResRoot, SceneFilePath.GetBaseDir())];
            total = files.Length;
            ready = true;
            var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                dumped += DumpFile(file, folder, written) ? 1 : 0;
            }
        }
        finally
        {
            GD.Print($"DUMPED {dumped}/{total}");

            // The pair mode's finalizer crash workaround, for the same reason.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GetTree().Quit(ready && total > 0 && dumped == total ? 0 : 1);
        }
    }

    // The argument's folder, created when missing, or null after a setup FAIL line when it is not an absolute path.
    private static string? DumpFolder(string argument)
    {
        string folder = argument[DumpPrefix.Length..];
        if (!Path.IsPathFullyQualified(folder))
        {
            GD.Print($"DUMP setup FAIL {DumpPrefix}<folder> needs an absolute folder, got `{folder}`");
            return null;
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    // Every scene and resource file under folder, depth first: a folder's files, then its subfolders, each in ordinal order.
    private static IEnumerable<string> ProjectFiles(string folder, string compareFolder)
    {
        IEnumerable<string> files = DirAccess
            .GetFilesAt(folder)
            .Where(file => DumpedExtensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal);
        foreach (string file in files)
        {
            yield return JoinPath(folder, file);
        }

        foreach (string name in DirAccess.GetDirectoriesAt(folder).Order(StringComparer.Ordinal))
        {
            string child = JoinPath(folder, name);
            if (WalkSkips(child, name, compareFolder) || OutsideProject(folder, child, name))
            {
                continue;
            }

            foreach (string file in ProjectFiles(child, compareFolder))
            {
                yield return file;
            }
        }
    }

    // The folders the walk leaves out: res://addons, dot folders, folders holding a .gdignore and the Compare scene's own.
    private static bool WalkSkips(string folder, string name, string compareFolder) =>
        name.StartsWith('.')
        || string.Equals(folder, ResRoot + "addons", StringComparison.Ordinal)
        || string.Equals(folder, compareFolder, StringComparison.Ordinal)
        || Godot.FileAccess.FileExists(folder + "/.gdignore");

    // The folders that are not this project's own files, skipped as the CLI's convert walk and Godot's scan skip them: a
    // folder holding its own project.godot, bin and obj beside a .csproj, and a link or junction.
    private static bool OutsideProject(string parent, string folder, string name) =>
        Godot.FileAccess.FileExists(folder + "/project.godot")
        || (name is "bin" or "obj" && DirAccess.GetFilesAt(parent).Any(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        || File.GetAttributes(ProjectSettings.GlobalizePath(folder)).HasFlag(FileAttributes.ReparsePoint);

    private static string JoinPath(string folder, string name) => folder.EndsWith('/') ? folder + name : $"{folder}/{name}";

    // Loads and dumps one file, writes its dump (or its LOAD FAIL line) under folder, and prints its DUMP line. A file
    // whose dump path an earlier file of the run already wrote (a/x.tscn and a/x.xscn) fails and writes nothing.
    private static bool DumpFile(string path, string folder, Dictionary<string, string> written)
    {
        string relative = path[ResRoot.Length..];
        string extension = Path.GetExtension(relative);
        bool scene = extension.Equals(".tscn", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xscn", StringComparison.OrdinalIgnoreCase);
        string dumpPath = relative[..^extension.Length] + (scene ? ".scene.dump" : ".resource.dump");
        if (!written.TryAdd(dumpPath, relative))
        {
            GD.Print($"DUMP {relative} FAIL collides with {written[dumpPath]}");
            return false;
        }

        string failure;
        List<string> lines;
        try
        {
            failure = scene ? DumpScene(path, extension[1..], out lines) : LoadResourceDump(path, extension[1..], out lines);
        }
        // A file that throws fails with the exception's text, as in the pair mode; the remaining files still run.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = $"threw {exception.GetType().Name}: {exception.Message}";
            lines = [];
        }

        string target = Path.Combine(folder, dumpPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllLines(target, failure.Length == 0 ? lines : [$"LOAD FAIL {failure}"]);
        GD.Print(failure.Length == 0 ? $"DUMP {relative} OK" : $"DUMP {relative} FAIL {failure}");
        return failure.Length == 0;
    }

    // The expected pair count, from the compare script's --expected-pairs=<n> user argument, or 0 when absent or invalid.
    private static int ExpectedPairs()
    {
        const string Prefix = "--expected-pairs=";
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (
                argument.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(argument[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count > 0
            )
            {
                return count;
            }
        }

        return 0;
    }

    // The fixtures in res://cases/ that have both a source and a golden, sorted ordinal within each kind. A stem that holds
    // both a scene file and a resource file, or a file without its partner, prints a setup FAIL line and empties the list.
    private static Pair[] PairNames()
    {
        string[] files = DirAccess.GetFilesAt(CasesPath);
        bool failed = false;
        HashSet<string> scene = [.. StemsOf(files, ".tscn"), .. StemsOf(files, ".xscn")];
        HashSet<string> resource = [.. StemsOf(files, ".tres"), .. StemsOf(files, ".xres")];
        foreach (string stem in scene.Intersect(resource).Order(StringComparer.Ordinal))
        {
            GD.Print($"COMPARE setup FAIL {stem} is both a scene and a resource");
            failed = true;
        }

        var pairs = new List<Pair>();
        foreach ((PairKind kind, string source, string converted) in Kinds)
        {
            HashSet<string> sources = StemsOf(files, source);
            HashSet<string> goldens = StemsOf(files, converted);
            string[] unpaired =
            [
                .. sources.Except(goldens).Select(name => name + source),
                .. goldens.Except(sources).Select(name => name + converted),
            ];
            foreach (string file in unpaired.Order(StringComparer.Ordinal))
            {
                GD.Print($"COMPARE setup FAIL unpaired {file}");
                failed = true;
            }

            pairs.AddRange(sources.Intersect(goldens).Order(StringComparer.Ordinal).Select(name => new Pair(name, kind, source, converted)));
        }

        return failed ? [] : [.. pairs];
    }

    private static HashSet<string> StemsOf(string[] files, string extension) =>
        [.. files.Where(file => file.EndsWith(extension, StringComparison.Ordinal)).Select(file => file[..^extension.Length])];

    private static bool Report(Pair pair)
    {
        string failure;
        try
        {
            failure = FailureOf(pair);
        }
        // A fixture that throws fails with the exception's text; the remaining fixtures still run.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = $"threw {exception.GetType().Name}: {exception.Message}";
        }

        GD.Print(failure.Length == 0 ? $"COMPARE {pair.Name} PASS" : $"COMPARE {pair.Name} FAIL {failure}");
        return failure.Length == 0;
    }

    // The first line where the two dumps differ, or an empty string when they are equal. Each dump is also written beside
    // the fixture under its own extension, as <name>.tscn.dump and <name>.xscn.dump or <name>.tres.dump and
    // <name>.xres.dump, so a failure can be read whole.
    private static string FailureOf(Pair pair)
    {
        string sourceFailure;
        string convertedFailure;
        List<string> source;
        List<string> converted;
        string sourceSide;
        string convertedSide;
        if (pair.Kind == PairKind.Scene)
        {
            (sourceSide, convertedSide) = ("tscn", "xscn");
            sourceFailure = DumpScene(CasesPath + pair.Name + pair.Source, sourceSide, out source);
            convertedFailure = DumpScene(CasesPath + pair.Name + pair.Converted, convertedSide, out converted);
        }
        else
        {
            (sourceSide, convertedSide) = ("tres", "xres");
            sourceFailure = LoadResourceDump(CasesPath + pair.Name + pair.Source, sourceSide, out source);
            convertedFailure = LoadResourceDump(CasesPath + pair.Name + pair.Converted, convertedSide, out converted);
        }

        if (sourceFailure.Length > 0 || convertedFailure.Length > 0)
        {
            return string.Join("; ", new[] { sourceFailure, convertedFailure }.Where(failure => failure.Length > 0));
        }

        File.WriteAllLines(ProjectSettings.GlobalizePath(CasesPath + pair.Name + pair.Source + ".dump"), source);
        File.WriteAllLines(ProjectSettings.GlobalizePath(CasesPath + pair.Name + pair.Converted + ".dump"), converted);
        return FirstDifference(source, converted, sourceSide, convertedSide);
    }

    private static string FirstDifference(List<string> source, List<string> converted, string sourceSide, string convertedSide)
    {
        for (int line = 0; line < Math.Max(source.Count, converted.Count); line++)
        {
            string fromSource = line < source.Count ? source[line] : "<end>";
            string fromConverted = line < converted.Count ? converted[line] : "<end>";
            if (!string.Equals(fromSource, fromConverted, StringComparison.Ordinal))
            {
                return $"line {line + 1}: {sourceSide} `{fromSource}` {convertedSide} `{fromConverted}`";
            }
        }

        return "";
    }

    // Loads and instantiates one side; returns why it could not, or an empty string with the dump in lines.
    private static string DumpScene(string path, string side, out List<string> lines)
    {
        lines = [];
        PackedScene? scene = ResourceLoader.Load<PackedScene>(path, "PackedScene", ResourceLoader.CacheMode.Ignore);
        if (scene is null)
        {
            return $"{side} did not load";
        }

        Node? root = scene.Instantiate();
        if (root is null)
        {
            return $"{side} did not instantiate";
        }

        try
        {
            lines = new SceneDump(scene, root).Lines();
            return "";
        }
        finally
        {
            root.Free();
        }
    }

    // Loads one resource side; returns why it could not, or an empty string with the dump in lines.
    private static string LoadResourceDump(string path, string side, out List<string> lines)
    {
        lines = [];
        Resource? resource = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore);
        if (resource is null)
        {
            return $"{side} did not load";
        }

        lines = new SceneDump(resource).Lines();
        return "";
    }

    // A resource or scene path with its .tscn, .xscn, .tres or .xres extension removed, so both sides print the same path.
    [GeneratedRegex(@"\.(tscn|xscn|tres|xres)(?=::|$)", RegexOptions.CultureInvariant)]
    private static partial Regex PathExtension();

    private static string StripExtension(string path) => PathExtension().Replace(path, "");

    /// <summary>
    /// How a resource is named in the dump: <c>local:&lt;class&gt;</c> without a path, <c>&lt;file&gt;::*</c> for a
    /// built-in sub-resource, else its path; extensions removed throughout.
    /// </summary>
    private static string ResourceName(Resource resource)
    {
        string path = resource.ResourcePath;
        int separator = path.IndexOf("::", StringComparison.Ordinal);
        return path.Length == 0 ? $"local:{resource.GetClass()}"
            : separator >= 0 ? $"{StripExtension(path[..separator])}::*"
            : StripExtension(path);
    }

    /// <summary>The canonical dump of one packed scene and the tree it instantiates, or of one resource and its properties.</summary>
    private sealed class SceneDump(PackedScene? scene, Node? root, Resource? resource)
    {
        // A scene side: the packed state and the tree it instantiates.
        public SceneDump(PackedScene scene, Node root)
            : this(scene, root, null) { }

        // A resource side: the resource's class, then its stored properties.
        public SceneDump(Resource resource)
            : this(null, null, resource) { }

        private const string CallableParts = "[v.get_object(), v.get_method(), v.get_bound_arguments(), v.get_unbound_arguments_count()]";

        // The property a script stores its source text in, whose path literals the conversion rewrites.
        private const string SourceProperty = "script/source";
        private const string ArrayTyping = "[v.is_typed(), v.get_typed_builtin(), v.get_typed_class_name(), v.get_typed_script()]";
        private const string DictionaryTyping =
            "[v.is_typed(), v.get_typed_key_builtin(), v.get_typed_key_class_name(), v.get_typed_key_script(),"
            + " v.get_typed_value_builtin(), v.get_typed_value_class_name(), v.get_typed_value_script()]";

        private readonly List<string> lines = [];
        private readonly List<List<string>> connections = [];
        private readonly Dictionary<ulong, int> visited = [];

        public List<string> Lines()
        {
            if (resource is not null)
            {
                DumpResource(resource);
                return lines;
            }

            DumpState();
            DumpNode(root!);
            foreach (List<string> connection in connections.OrderBy(block => block[0], StringComparer.Ordinal))
            {
                lines.AddRange(connection);
            }

            return lines;
        }

        private string PathOf(Node node) => root!.GetPathTo(node).ToString();

        // The resource's class, then its stored properties; the resource itself is visit 1, so a reference back to it is seen.
        private void DumpResource(Resource top)
        {
            lines.Add($"resource {top.GetClass()}");
            visited[top.GetInstanceId()] = 1;
            DumpProperties(top, "prop", "", "", lines);
        }

        // The packed state as stored: the base scene, each state node's instance, placeholder flag and stored property
        // names, then each node's unique id from the bundled node_ids, in the state's node order.
        private void DumpState()
        {
            PackedScene packed = scene!;
            SceneState state = packed.GetState();
            GodotDictionary bundled = packed.Get("_bundled").AsGodotDictionary();
            string baseScene = bundled.TryGetValue("base_scene", out Variant baseIndex)
                ? ResourceName(bundled["variants"].AsGodotArray()[baseIndex.AsInt32()].As<Resource>())
                : "-";
            lines.Add($"base {baseScene}");
            int[] ids = bundled.TryGetValue("node_ids", out Variant idValue) ? idValue.AsInt32Array() : [];
            for (int node = 0; node < state.GetNodeCount(); node++)
            {
                PackedScene? instance = state.GetNodeInstance(node);
                IEnumerable<string> properties = Enumerable
                    .Range(0, state.GetNodePropertyCount(node))
                    .Select(property => state.GetNodePropertyName(node, property).ToString())
                    .Order(StringComparer.Ordinal);
                string instanceText = instance is null ? "-" : ResourceName(instance);
                string placeholder = state.IsNodeInstancePlaceholder(node).ToString();
                lines.Add($"state {state.GetNodePath(node)} instance={instanceText} placeholder={placeholder} props={string.Join(",", properties)}");
            }

            for (int node = 0; node < state.GetNodeCount(); node++)
            {
                lines.Add($"ids {state.GetNodePath(node)} {(node < ids.Length ? ids[node].ToString(CultureInfo.InvariantCulture) : "-")}");
            }
        }

        private void DumpNode(Node node)
        {
            string path = PathOf(node);
            string owner = node.Owner is null ? "-" : PathOf(node.Owner);
            string sceneFile = node.SceneFilePath.Length == 0 ? "" : $" scene_file={StripExtension(node.SceneFilePath)}";
            string placeholder = node is InstancePlaceholder held ? $" placeholder={StripExtension(held.GetInstancePath())}" : "";
            string facts = $"class={node.GetClass()} index={node.GetIndex()} owner={owner} unique={node.UniqueNameInOwner}";
            lines.Add($"node {path} {facts}{sceneFile}{placeholder}");
            IEnumerable<string> groups = node.GetGroups()
                .Select(group => group.ToString())
                .Where(group => !group.StartsWith('_'))
                .Order(StringComparer.Ordinal);
            foreach (string group in groups)
            {
                lines.Add($"group {path} {group}");
            }

            DumpProperties(node, "prop", path, "", lines);
            CollectConnections(node, path);
            if (node != root && root!.IsEditableInstance(node))
            {
                lines.Add($"editable {path}");
            }

            foreach (Node child in node.GetChildren())
            {
                DumpNode(child);
            }
        }

        private void DumpProperties(GodotObject owner, string kind, string path, string keyPrefix, List<string> into)
        {
            IEnumerable<string> names = owner
                .GetPropertyList()
                .Where(property => (property["usage"].AsInt64() & (long)PropertyUsageFlags.Storage) != 0)
                .Select(property => property["name"].AsString())
                .Order(StringComparer.Ordinal);
            foreach (string name in names)
            {
                var firstVisits = new List<(Resource, string)>();
                Variant value = owner.Get(name);
                if (owner is Script && name == SourceProperty && value.VariantType == Variant.Type.String)
                {
                    value = UndoPathRewrite(value.AsString());
                }

                into.Add($"{kind} {path} {keyPrefix}{name}={ValueText(keyPrefix + name, value, firstVisits)}");
                DumpFirstVisits(path, firstVisits, into);
            }
        }

        // The conversion rewrites exact-path literals inside scripts, `.tscn` to `.xscn` and `.tres` to `.xres`; undone
        // here, the original and converted source texts read alike.
        private static string UndoPathRewrite(string source) =>
            source.Replace(".xscn", ".tscn", StringComparison.Ordinal).Replace(".xres", ".tres", StringComparison.Ordinal);

        // The subprop lines of each resource a value named for the first time, after the value's own line.
        private void DumpFirstVisits(string path, List<(Resource Resource, string Key)> firstVisits, List<string> into)
        {
            foreach ((Resource resource, string key) in firstVisits)
            {
                DumpProperties(resource, "subprop", path, key + ".", into);
            }
        }

        private string ValueText(string key, Variant value, List<(Resource, string)> firstVisits) =>
            value.VariantType switch
            {
                Variant.Type.Object => ObjectText(key, value.AsGodotObject(), firstVisits),
                Variant.Type.Array when HoldsObject(value) => ArrayText(key, value.AsGodotArray(), firstVisits),
                Variant.Type.Dictionary when HoldsObject(value) => DictionaryText(key, value.AsGodotDictionary(), firstVisits),
                _ => GD.VarToStr(value),
            };

        private string ObjectText(string key, GodotObject? value, List<(Resource, string)> firstVisits) =>
            value switch
            {
                null => "null",
                Node node => PathOf(node),
                // A scene's packing order is not the scene, so a scene value is named and never opened.
                PackedScene packed => $"res({ResourceName(packed)})",
                Resource resource => ResourceText(key, resource, firstVisits),
                _ => $"obj({value.GetClass()})",
            };

        // A resource is dumped once, at its first visit; a later reference names that visit's ordinal.
        private string ResourceText(string key, Resource resource, List<(Resource, string)> firstVisits)
        {
            if (visited.TryGetValue(resource.GetInstanceId(), out int ordinal))
            {
                return $"res(seen:{ordinal})";
            }

            visited[resource.GetInstanceId()] = visited.Count + 1;
            firstVisits.Add((resource, key));
            return $"res({ResourceName(resource)})";
        }

        // An array that holds an object is written element by element; a typed one keeps its element type in front.
        private string ArrayText(string key, GodotArray array, List<(Resource, string)> firstVisits)
        {
            var items = new List<string>();
            for (int index = 0; index < array.Count; index++)
            {
                items.Add(ValueText($"{key}[{index}]", array[index], firstVisits));
            }

            GodotArray typing = Evaluate(ArrayTyping, array);
            string prefix = typing[0].AsBool() ? $"Array[{TypeText(typing, 1)}]" : "";
            return $"{prefix}[{string.Join(", ", items)}]";
        }

        // A dictionary that holds an object is written pair by pair in its own order. A key's resources are dumped under
        // "<prop>{#<pair>}", a value's under "<prop>[<key text>]".
        private string DictionaryText(string key, GodotDictionary dictionary, List<(Resource, string)> firstVisits)
        {
            var pairs = new List<string>();
            int pair = 0;
            foreach ((Variant entryKey, Variant entryValue) in dictionary)
            {
                string keyText = ValueText($"{key}{{#{pair}}}", entryKey, firstVisits);
                pairs.Add($"{keyText}: {ValueText($"{key}[{keyText}]", entryValue, firstVisits)}");
                pair++;
            }

            GodotArray typing = Evaluate(DictionaryTyping, dictionary);
            string prefix = typing[0].AsBool() ? $"Dictionary[{TypeText(typing, 1)}, {TypeText(typing, 4)}]" : "";
            return $"{prefix}{{{string.Join(", ", pairs)}}}";
        }

        // A container element type from three typing values at start: the builtin type, the class name and the script.
        // An untyped part prints as Variant.
        private static string TypeText(GodotArray typing, int start)
        {
            var builtin = (Variant.Type)typing[start].AsInt32();
            string className = typing[start + 1].AsString();
            Resource? script = typing[start + 2].As<Resource>();
            string type =
                builtin == Variant.Type.Nil ? "Variant"
                : className.Length > 0 ? className
                : builtin.ToString();
            return script is null ? type : $"{type}({ResourceName(script)})";
        }

        private static bool HoldsObject(Variant value) =>
            value.VariantType switch
            {
                Variant.Type.Object => true,
                Variant.Type.Array => value.AsGodotArray().Any(HoldsObject),
                Variant.Type.Dictionary => value.AsGodotDictionary().Any(pair => HoldsObject(pair.Key) || HoldsObject(pair.Value)),
                _ => false,
            };

        private void CollectConnections(Node node, string path)
        {
            foreach (GodotDictionary signal in node.GetSignalList())
            {
                StringName signalName = signal["name"].AsStringName();
                foreach (GodotDictionary connection in node.GetSignalConnectionList(signalName))
                {
                    long flags = connection["flags"].AsInt64();
                    if ((flags & (long)ConnectFlags.Persist) != 0)
                    {
                        connections.Add(ConnectionBlock(path, signalName, connection["callable"], flags));
                    }
                }
            }
        }

        // A connection's line, then the subprop lines of any resource its bound arguments name for the first time.
        private List<string> ConnectionBlock(string path, StringName signal, Variant callable, long flags)
        {
            GodotArray parts = Evaluate(CallableParts, callable);
            string target = parts[0].AsGodotObject() switch
            {
                Node node => PathOf(node),
                GodotObject other => $"obj({other.GetClass()})",
                null => "null",
            };
            string method = parts[1].AsString();
            string bindsKey = $"{signal}->{method}.binds";
            var firstVisits = new List<(Resource, string)>();
            string binds = parts[2].AsGodotArray().Count == 0 ? "" : ValueText(bindsKey, parts[2], firstVisits);
            List<string> block = [$"conn {path} {signal} -> {target} {method} flags={flags} binds={binds} unbinds={parts[3].AsInt32()}"];
            DumpFirstVisits(path, firstVisits, block);
            return block;
        }

        // GodotSharp exposes neither a bound callable's arguments nor a container's element types, so both are read
        // through a Godot expression over the value, named "v" in the expression.
        private static GodotArray Evaluate(string text, Variant value)
        {
            using var expression = new Expression();
            Error parsed = expression.Parse(text, ["v"]);
            if (parsed != Error.Ok)
            {
                throw new InvalidOperationException($"the expression {text} did not parse: {expression.GetErrorText()}");
            }

            Variant result = expression.Execute(new GodotArray { value });
            if (expression.HasExecuteFailed())
            {
                throw new InvalidOperationException($"the expression {text} failed: {expression.GetErrorText()}");
            }

            return result.AsGodotArray();
        }
    }
}
