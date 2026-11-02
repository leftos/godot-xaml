using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GodotXaml.Addon;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.AddonChecks;

/// <summary>
/// Runs the addon's headless checks inside Godot, prints <c>CHECK &lt;id&gt; PASS</c> or <c>CHECK &lt;id&gt; FAIL &lt;detail&gt;</c> for each,
/// then <c>DONE &lt;passed&gt;/&lt;total&gt;</c>, and quits with exit code 0 only when every check passed.
/// </summary>
public partial class Checks : Node
{
    private const string BrokenScenePath = "res://checks/broken.xscn";
    private const string ProbeSavePath = "user://probe.xscn";
    private const string CleanScene = "<Control Name=\"Root\">\n  <Button Name=\"Ok\" />\n</Control>\n";
    private const string MinimalTscn = "[gd_scene format=3]\n\n[node name=\"Probe\" type=\"Node\"]\n";
    private const string CasesPath = "res://checks/cases/";
    private const string ChildScenePath = CasesPath + "child.xscn";
    private const string BoxPath = CasesPath + "box.xres";
    private const string ThemePath = CasesPath + "theme.xres";
    private const string HolderPath = CasesPath + "holder.xscn";
    private const string ParentPath = CasesPath + "parent.xscn";
    private const string SavesFolder = "user://gx-saves/";
    private const string OutFolder = "res://checks/out/";

    // Two users of one material, so the conversion keeps it in x:Resources rather than inlining it.
    private const string KeysScene =
        "<Node2D xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Resources>\n"
        + "    <CanvasItemMaterial x:Key=\"HandKey\" LightMode=\"Unshaded\" />\n"
        + "  </x:Resources>\n"
        + "  <Sprite2D Name=\"One\" Material=\"{HandKey}\" />\n"
        + "  <Sprite2D Name=\"Two\" Material=\"{HandKey}\" />\n"
        + "</Node2D>\n";

    private static readonly string LoaderTempFolder = Path.Combine(Path.GetTempPath(), "godot-xaml");

    private static readonly string[] ResourceTypesToHandle = ["StyleBox", "Theme"];

    // A hang guard, not a time budget: the threaded load of two small scenes takes a few frames, and checks.ps1 quits the
    // run after 600.
    private const int ThreadedLoadFrameGuard = 300;

    private static int tempPathsBefore;

    /// <inheritdoc/>
    public override async void _Ready()
    {
        (string Id, Func<string> Run)[] checks =
        [
            ("C1", CoreLoads),
            ("C2", ParsesInsideGodot),
            ("C3", Registered),
            ("C4", ParseErrorFailsTheLoad),
            ("C5", CustomSaverIsChosen),
            ("C6", AbsoluteOsPathLoads),
            ("C7", LoadsAScene),
            ("C8", LoadsAnInstanceOfAnotherXscn),
            ("C9", LoadsAResource),
            ("C10", XresReportsItsRootClass),
            ("C11", HandlesEveryResourceType),
            ("C12", ConversionErrorFailsTheLoad),
            ("C13", TempFilesAreRemoved),
            ("C14", SubResourcesNameTheXscn),
            ("C15", WalkStaysInTheFile),
            ("C16", IgnoreDeepReloadsADependency),
            ("C19", SavesAScene),
            ("C20", SavesAResource),
            ("C21", SavingTwiceGivesTheSameBytes),
            ("C22", SavingKeepsResourceKeys),
            ("C23", RecognizesEveryResourceButAScript),
            ("C24", SetUidWritesTheSidecar),
            ("C25", SetUidRefusesOtherExtensions),
            ("C26", SavingOverUnparsableTextWritesAParsableFile),
            ("C27", SaveToAMissingFolderWritesNothing),
            ("C28", ExtensionsSplitByType),
            ("C29", ReportsDependencies),
            ("C30", RenamesDependencies),
            ("C32", RelativeSpellingStaysInTheProject),
        ];
        DeleteFolder(SavesFolder);
        int total = checks.Length + 2;
        XscnLoadRoute.RecordTempPaths = true;
        tempPathsBefore = XscnLoadRoute.TempPaths.Count;
        int passed = 0;
        try
        {
            foreach ((string id, Func<string> run) in checks)
            {
                passed += Report(id, run) ? 1 : 0;
            }

            string threadedLoadFailure = await FailureOf(() => ThreadedLoad(ParentPath, ThreadedResultFailure));
            passed += Report("C18", () => threadedLoadFailure) ? 1 : 0;
            string threadedHolderFailure = await FailureOf(() => ThreadedLoad(HolderPath, ThreadedHolderFailure));
            passed += Report("C31", () => threadedHolderFailure) ? 1 : 0;
        }
        finally
        {
            GD.Print($"DONE {passed}/{total}");
            GetTree().Quit(passed == total ? 0 : 1);
        }
    }

    // An asynchronous check that throws fails with the exception's text, as a synchronous one does in Report.
    private static async Task<string> FailureOf(Func<Task<string>> run)
    {
        try
        {
            return await run();
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return $"threw {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static bool Report(string id, Func<string> run)
    {
        string failure;
        try
        {
            failure = run();
        }
        // A check that throws fails with the exception's text; the remaining checks still run.
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = $"threw {exception.GetType().Name}: {exception.Message}";
        }

        GD.Print(failure.Length == 0 ? $"CHECK {id} PASS" : $"CHECK {id} FAIL {failure}");
        return failure.Length == 0;
    }

    private static string CoreLoads()
    {
        var metadata = GodotMetadata.LoadBundled("4.7");
        return metadata.FindClass("Button") is null ? "the bundled 4.7 metadata has no class Button" : "";
    }

    private static string ParsesInsideGodot()
    {
        XscnParseResult result = XscnParser.Parse(CleanScene, XscnDocumentKind.Scene, GodotMetadata.LoadBundled("4.7"));
        return result.Diagnostics.IsEmpty ? "" : "diagnostics: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}"));
    }

    private static string Registered()
    {
        string[] loaderExtensions = ResourceLoader.GetRecognizedExtensionsForType("PackedScene");
        string[] saverExtensions = ResourceSaver.GetRecognizedExtensions(new PackedScene());
        var missing = new List<string>();
        if (!loaderExtensions.Contains("xscn"))
        {
            missing.Add($"the loaders' PackedScene extensions [{string.Join(", ", loaderExtensions)}] lack xscn");
        }

        if (!saverExtensions.Contains("xscn"))
        {
            missing.Add($"the savers' PackedScene extensions [{string.Join(", ", saverExtensions)}] lack xscn");
        }

        return string.Join("; ", missing);
    }

    private static string ParseErrorFailsTheLoad()
    {
        int callsBefore = XscnLoading.LoadCallCount;
        Resource loaded = ResourceLoader.Load(BrokenScenePath, "", ResourceLoader.CacheMode.Ignore);
        int calls = XscnLoading.LoadCallCount - callsBefore;
        if (loaded is not null)
        {
            return $"loading {BrokenScenePath} returned a {loaded.GetClass()}, not null";
        }

        return calls == 1 ? "" : $"the custom loader was called {calls} times, not once";
    }

    private static string CustomSaverIsChosen()
    {
        var root = new Node { Name = "Probe" };
        var scene = new PackedScene();
        Error packError = scene.Pack(root);
        root.Free();
        if (packError != Error.Ok)
        {
            return $"PackedScene.Pack returned {packError}";
        }

        int callsBefore = XscnFormatSaver.SaveCallCount;
        Error saveError = ResourceSaver.Save(scene, ProbeSavePath);
        int calls = XscnFormatSaver.SaveCallCount - callsBefore;
        if (saveError != Error.Ok)
        {
            return $"ResourceSaver.Save({ProbeSavePath}) returned {saveError}, not Ok (custom saver calls: {calls})";
        }

        return calls == 1 ? "" : $"the custom saver was called {calls} times, not once";
    }

    private static string AbsoluteOsPathLoads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"godot-xaml-probe-{Guid.NewGuid():N}.tscn").Replace('\\', '/');
        File.WriteAllText(path, MinimalTscn);
        try
        {
            Resource loaded = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore);
            return loaded is PackedScene ? "" : $"loading {path} returned {loaded?.GetClass() ?? "null"}, not a PackedScene";
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string LoadsAScene()
    {
        const string path = CasesPath + "scene.xscn";
        if (ResourceLoader.Load(path) is not PackedScene scene)
        {
            return $"loading {path} did not return a PackedScene";
        }

        Resource again = ResourceLoader.Load(path);
        GD.Print($"MEASURE C7 a second Load of {path} returns the same object: {again?.GetInstanceId() == scene.GetInstanceId()}");
        Node root = scene.Instantiate();
        try
        {
            var failures = new List<string>();
            if (root is not Control)
            {
                failures.Add($"the root is a {root.GetClass()}, not a Control");
            }

            failures.AddRange(ClassMismatches(root, [("Label", "Label"), ("Panel", "Panel"), ("Button", "Button")]));
            if (root.GetNodeOrNull<Label>("Label") is { } label && label.Text != "hi")
            {
                failures.Add($"the label's text is \"{label.Text}\", not \"hi\"");
            }

            failures.Add(InlineStyleBoxFailure(root.GetNodeOrNull<Panel>("Panel")));
            failures.Add(PressedConnectionFailure(root.GetNodeOrNull<Button>("Button")));
            return string.Join("; ", failures.Where(f => f.Length > 0));
        }
        finally
        {
            root.Free();
        }
    }

    private static IEnumerable<string> ClassMismatches(Node root, (string Path, string Class)[] expected) =>
        expected
            .Select(e => (e.Path, e.Class, Node: root.GetNodeOrNull(e.Path)))
            .Where(e => e.Node is null || e.Node.GetClass() != e.Class)
            .Select(e => $"{e.Path} is {e.Node?.GetClass() ?? "missing"}, not a {e.Class}");

    private static string InlineStyleBoxFailure(Panel? panel)
    {
        if (panel?.Get("theme_override_styles/panel").AsGodotObject() is not StyleBoxFlat box)
        {
            return "the panel has no StyleBoxFlat override for panel";
        }

        GD.Print($"MEASURE C7 the inline StyleBoxFlat's ResourcePath is \"{box.ResourcePath}\"");
        return box.BgColor == new Color(1, 0, 0, 1) ? "" : $"the stylebox's BgColor is {box.BgColor}, not (1, 0, 0, 1)";
    }

    private static string PressedConnectionFailure(Button? button)
    {
        if (button is null)
        {
            return "";
        }

        bool found = button
            .GetSignalConnectionList("pressed")
            .Any(c => c["callable"].AsCallable().Method == "OnPressed" && (c["flags"].AsInt64() & (long)ConnectFlags.Deferred) != 0);
        return found ? "" : "the button's pressed signal has no deferred connection to OnPressed";
    }

    private static string LoadsAnInstanceOfAnotherXscn()
    {
        const string path = CasesPath + "parent.xscn";
        if (ResourceLoader.Load(path) is not PackedScene scene)
        {
            return $"loading {path} did not return a PackedScene";
        }

        Node root = scene.Instantiate();
        try
        {
            Node? instance = root.GetNodeOrNull("Inst");
            if (instance is null)
            {
                return "the scene has no node Inst";
            }

            if (instance.SceneFilePath != ChildScenePath)
            {
                return $"Inst's SceneFilePath is \"{instance.SceneFilePath}\", not \"{ChildScenePath}\"";
            }

            Node2D? inner = root.GetNodeOrNull<Node2D>("Inst/Inner");
            return inner switch
            {
                null => "the scene has no Node2D Inst/Inner",
                { Visible: true } => "Inst/Inner is visible; the override set it hidden",
                _ => "",
            };
        }
        finally
        {
            root.Free();
        }
    }

    private static string LoadsAResource()
    {
        Resource loaded = ResourceLoader.Load(BoxPath);
        if (loaded is not StyleBoxFlat box)
        {
            return $"loading {BoxPath} returned {loaded?.GetClass() ?? "null"}, not a StyleBoxFlat";
        }

        var failures = new List<string>();
        if (box.BgColor != new Color(0, 1, 0, 1))
        {
            failures.Add($"its BgColor is {box.BgColor}, not (0, 1, 0, 1)");
        }

        if (box.CornerRadiusTopLeft != 4)
        {
            failures.Add($"its CornerRadiusTopLeft is {box.CornerRadiusTopLeft}, not 4");
        }

        return string.Join("; ", failures);
    }

    // Godot binds no ResourceLoader.get_resource_type for scripts (core/core_bind.cpp), so this asks the loaders directly;
    // that the engine routes the question to them is proved by the editor checks, not here. Each loader reports no type
    // for the other's extension.
    private static string XresReportsItsRootClass()
    {
        using var resourceLoader = new XresFormatLoader();
        using var sceneLoader = new XscnFormatLoader();
        (ResourceFormatLoader Loader, string Path, string Type)[] expected =
        [
            (resourceLoader, BoxPath, "StyleBoxFlat"),
            (resourceLoader, CasesPath + "notxml.xres", "Resource"),
            (resourceLoader, CasesPath + "scene.xscn", ""),
            (sceneLoader, CasesPath + "scene.xscn", "PackedScene"),
            (sceneLoader, BoxPath, ""),
        ];
        return string.Join(
            "; ",
            expected
                .Select(e => (e.Loader, e.Path, e.Type, Reported: e.Loader._GetResourceType(e.Path)))
                .Where(e => e.Reported != e.Type)
                .Select(e => $"{e.Loader.GetType().Name} reports type \"{e.Reported}\" for {e.Path}, not \"{e.Type}\"")
        );
    }

    private static string HandlesEveryResourceType()
    {
        string[] lacking = [.. ResourceTypesToHandle.Where(type => !ResourceLoader.GetRecognizedExtensionsForType(type).Contains("xres"))];
        return lacking.Length == 0 ? "" : $"the loaders' extensions for {string.Join(", ", lacking)} lack xres";
    }

    private static string ConversionErrorFailsTheLoad()
    {
        const string path = CasesPath + "badoverride.xscn";
        int callsBefore = XscnLoading.LoadCallCount;
        Resource loaded = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore);
        int calls = XscnLoading.LoadCallCount - callsBefore;
        if (loaded is not null)
        {
            return $"loading {path} returned a {loaded.GetClass()}, not null";
        }

        return calls == 1 ? "" : $"the custom loader was called {calls} times, not once";
    }

    private static string TempFilesAreRemoved()
    {
        string[] recorded = [.. XscnLoadRoute.TempPaths.Skip(tempPathsBefore)];
        if (recorded.Length == 0)
        {
            return "the loads of C7 to C9 recorded no temp file";
        }

        string[] left = [.. recorded.Where(File.Exists)];
        return left.Length == 0 ? "" : $"temp files left behind: [{string.Join(", ", left)}]";
    }

    private static string SubResourcesNameTheXscn()
    {
        const string scenePath = CasesPath + "scene.xscn";
        const string themePath = CasesPath + "theme.xres";
        if (ResourceLoader.Load(scenePath, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {scenePath} did not return a PackedScene";
        }

        if (ResourceLoader.Load(themePath, "", ResourceLoader.CacheMode.Ignore) is not Theme theme)
        {
            return $"loading {themePath} did not return a Theme";
        }

        string tempFolder = LoaderTempFolder.Replace('\\', '/');
        IEnumerable<string> failures = new[]
        {
            PathUnderFailure("the inline StyleBoxFlat", InlineStyleBoxPath(scene), scenePath),
            PathUnderFailure("the theme's font", theme.DefaultFont?.ResourcePath ?? "(none)", themePath),
        }
            .Where(f => f.Length > 0)
            .Concat(
                ReachablePaths(scene.Get("_bundled"))
                    .Concat(ReachablePaths(theme))
                    .Where(p => p.Contains(tempFolder, StringComparison.OrdinalIgnoreCase))
                    .Select(p => $"a reachable resource's path names the temp folder: \"{p}\"")
            );
        return string.Join("; ", failures);
    }

    private static string InlineStyleBoxPath(PackedScene scene)
    {
        Node root = scene.Instantiate();
        try
        {
            return root.GetNodeOrNull<Panel>("Panel")?.Get("theme_override_styles/panel").AsGodotObject() is Resource box
                ? box.ResourcePath
                : "(none)";
        }
        finally
        {
            root.Free();
        }
    }

    private static string PathUnderFailure(string what, string path, string file) =>
        path.StartsWith(file + "::", StringComparison.Ordinal) ? "" : $"{what}'s path is \"{path}\", not under {file}::";

    // The paths of every resource reachable from a value through resource properties, arrays and dictionaries.
    private static List<string> ReachablePaths(Variant start)
    {
        var paths = new List<string>();
        var visited = new HashSet<ulong>();
        var pending = new Stack<Variant>([start]);
        while (pending.TryPop(out Variant value))
        {
            Resource? resource = value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Resource : null;
            if (resource is null)
            {
                PushContainerItems(value, pending);
            }
            else if (visited.Add(resource.GetInstanceId()))
            {
                paths.Add(resource.ResourcePath);
                foreach (Godot.Collections.Dictionary property in resource.GetPropertyList())
                {
                    pending.Push(resource.Get(property["name"].AsStringName()));
                }
            }
        }

        return paths;
    }

    private static void PushContainerItems(Variant value, Stack<Variant> pending)
    {
        if (value.VariantType == Variant.Type.Array)
        {
            foreach (Variant item in value.AsGodotArray())
            {
                pending.Push(item);
            }
        }
        else if (value.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> pair in value.AsGodotDictionary())
            {
                pending.Push(pair.Key);
                pending.Push(pair.Value);
            }
        }
    }

    private static string WalkStaysInTheFile()
    {
        const string holderPath = CasesPath + "holder.xscn";
        const string themePath = CasesPath + "theme.xres";

        // Cached first, so the holder's load takes them from the cache and the count below is the holder's walk alone.
        if (ResourceLoader.Load(themePath) is not Theme || ResourceLoader.Load(BoxPath) is not StyleBoxFlat)
        {
            return $"loading {themePath} or {BoxPath} failed";
        }

        XscnLoadRoute.ResetWalkVisits();
        if (ResourceLoader.Load(holderPath, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {holderPath} did not return a PackedScene";
        }

        // The scene's own resources: the scene itself and the inline StyleBoxFlat.
        const int ownResources = 2;
        int visits = XscnLoadRoute.WalkVisits;
        string[] failures =
        [
            visits == ownResources ? "" : $"the walk entered {visits} resources, not the scene's own {ownResources}",
            .. ExternalPathFailures(scene, themePath),
        ];
        return string.Join("; ", failures.Where(f => f.Length > 0));
    }

    private static string[] ExternalPathFailures(PackedScene scene, string themePath)
    {
        Node root = scene.Instantiate();
        try
        {
            string fontPath = (root as Control)?.Theme?.DefaultFont?.ResourcePath ?? "(none)";
            string boxPath = root.GetNodeOrNull<Panel>("External")?.Get("theme_override_styles/panel").AsGodotObject() is Resource box
                ? box.ResourcePath
                : "(none)";
            return
            [
                PathUnderFailure("the external theme's font", fontPath, themePath),
                boxPath == BoxPath ? "" : $"the external stylebox's path is \"{boxPath}\", not \"{BoxPath}\"",
            ];
        }
        finally
        {
            root.Free();
        }
    }

    private static string IgnoreDeepReloadsADependency()
    {
        const string parentPath = CasesPath + "parent.xscn";
        const string plainInner = "<Node2D Name=\"Inner\" />";
        string childFile = ProjectSettings.GlobalizePath(ChildScenePath);
        string original = File.ReadAllText(childFile);
        if (!original.Contains(plainInner, StringComparison.Ordinal) || ResourceLoader.Load(parentPath) is not PackedScene)
        {
            return $"{ChildScenePath} has no {plainInner}, or {parentPath} did not load";
        }

        try
        {
            File.WriteAllText(childFile, original.Replace(plainInner, "<Node2D Name=\"Inner\" Position=\"5, 6\" />", StringComparison.Ordinal));
            if (ResourceLoader.Load(parentPath, "", ResourceLoader.CacheMode.IgnoreDeep) is not PackedScene scene)
            {
                return $"loading {parentPath} with IgnoreDeep did not return a PackedScene";
            }

            Node root = scene.Instantiate();
            Vector2? position = root.GetNodeOrNull<Node2D>("Inst/Inner")?.Position;
            root.Free();
            return position == new Vector2(5, 6) ? "" : $"Inst/Inner's position is {position?.ToString() ?? "(no node)"}, not the rewritten (5, 6)";
        }
        finally
        {
            File.WriteAllText(childFile, original);
        }
    }

    private static string SavesAScene()
    {
        const string sourcePath = CasesPath + "scene.xscn";
        const string savedPath = SavesFolder + "scene.xscn";
        if (ResourceLoader.Load(sourcePath) is not PackedScene scene)
        {
            return $"loading {sourcePath} did not return a PackedScene";
        }

        string failure = SaveAndParse(scene, savedPath, XscnDocumentKind.Scene, out _);
        if (failure.Length > 0)
        {
            return failure;
        }

        if (ResourceLoader.Load(savedPath, "", ResourceLoader.CacheMode.Ignore) is not PackedScene saved)
        {
            return $"loading {savedPath} did not return a PackedScene";
        }

        string expected = string.Join(", ", PreOrder(scene));
        string actual = string.Join(", ", PreOrder(saved));
        return expected == actual ? "" : $"the saved scene's nodes are [{actual}], not [{expected}]";
    }

    private static string SavesAResource()
    {
        const string sourcePath = CasesPath + "theme.xres";
        if (ResourceLoader.Load(sourcePath) is not Theme theme)
        {
            return $"loading {sourcePath} did not return a Theme";
        }

        string failure = SaveAndParse(theme, SavesFolder + "theme.xres", XscnDocumentKind.Resource, out XscnDocument? document);
        if (failure.Length > 0)
        {
            return failure;
        }

        string root = document?.Root is XscnResource { Type: XscnClassType type } ? type.Class.Name : "(no engine class)";
        return root == "Theme" ? "" : $"the saved resource's root tag is {root}, not Theme";
    }

    private static string SavingTwiceGivesTheSameBytes()
    {
        const string sourcePath = CasesPath + "scene.xscn";
        const string savedPath = SavesFolder + "twice.xscn";
        if (ResourceLoader.Load(sourcePath) is not PackedScene scene)
        {
            return $"loading {sourcePath} did not return a PackedScene";
        }

        byte[] first = SaveAndRead(scene, savedPath);
        byte[] second = SaveAndRead(scene, savedPath);
        return first.SequenceEqual(second)
            ? ""
            : $"the second save differs from the first:\n{Encoding.UTF8.GetString(first)}\n---\n{Encoding.UTF8.GetString(second)}";
    }

    private static string SavingKeepsResourceKeys()
    {
        const string path = SavesFolder + "keys.xscn";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        File.WriteAllText(ProjectSettings.GlobalizePath(path), KeysScene);
        if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {path} did not return a PackedScene";
        }

        string saved = Encoding.UTF8.GetString(SaveAndRead(scene, path));
        int keys = Occurrences(saved, "x:Key=\"HandKey\"");
        int uses = Occurrences(saved, "Material=\"{HandKey}\"");
        return keys > 0 && uses == 2 ? "" : $"the saved text has x:Key=\"HandKey\" {keys} times and Material=\"{{HandKey}}\" {uses} times:\n{saved}";
    }

    private static string RecognizesEveryResourceButAScript()
    {
        using var saver = new XscnFormatSaver();
        using var script = new GDScript();
        using var box = new StyleBoxFlat();
        var failures = new List<string>();
        if (saver._Recognize(script))
        {
            failures.Add("_Recognize is true for a GDScript");
        }

        if (!saver._Recognize(box))
        {
            failures.Add("_Recognize is false for a StyleBoxFlat");
        }

        return string.Join("; ", failures);
    }

    private static string SetUidWritesTheSidecar()
    {
        const string path = SavesFolder + "scene.xscn";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        string sidecar = ProjectSettings.GlobalizePath(path) + ".uid";
        File.Delete(sidecar);
        using var saver = new XscnFormatSaver();
        long id = ResourceUid.CreateId();
        Error error = saver._SetUid(path, id);
        if (error != Error.Ok)
        {
            return $"_SetUid({path}) returned {error}";
        }

        if (!File.Exists(sidecar))
        {
            return $"_SetUid({path}) wrote no {sidecar}";
        }

        string firstLine = File.ReadLines(sidecar).FirstOrDefault() ?? "(empty)";
        string expected = ResourceUid.IdToText(id);
        return firstLine == expected ? "" : $"the sidecar's first line is \"{firstLine}\", not \"{expected}\"";
    }

    // The save and rename checks start from an empty folder on every run.
    private static void DeleteFolder(string resFolder)
    {
        string folder = ProjectSettings.GlobalizePath(resFolder);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string SetUidRefusesOtherExtensions()
    {
        const string path = SavesFolder + "x.gd";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        string sidecar = ProjectSettings.GlobalizePath(path) + ".uid";
        using var saver = new XscnFormatSaver();
        Error error = saver._SetUid(path, ResourceUid.CreateId());
        var failures = new List<string>();
        if (error != Error.FileUnrecognized)
        {
            failures.Add($"_SetUid({path}) returned {error}, not FileUnrecognized");
        }

        if (File.Exists(sidecar))
        {
            failures.Add($"_SetUid({path}) wrote {sidecar}");
        }

        return string.Join("; ", failures);
    }

    private static string SavingOverUnparsableTextWritesAParsableFile()
    {
        const string sourcePath = CasesPath + "scene.xscn";
        const string savedPath = SavesFolder + "unparsable.xscn";
        if (ResourceLoader.Load(sourcePath) is not PackedScene scene)
        {
            return $"loading {sourcePath} did not return a PackedScene";
        }

        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        File.WriteAllText(ProjectSettings.GlobalizePath(savedPath), "<Control Name=\"Root\"><unclosed>");
        return SaveAndParse(scene, savedPath, XscnDocumentKind.Scene, out _);
    }

    private static string SaveToAMissingFolderWritesNothing()
    {
        const string sourcePath = CasesPath + "scene.xscn";
        const string missingFolder = SavesFolder + "missing/";
        const string savedPath = missingFolder + "scene.xscn";
        if (ResourceLoader.Load(sourcePath) is not PackedScene scene)
        {
            return $"loading {sourcePath} did not return a PackedScene";
        }

        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        Error error = ResourceSaver.Save(scene, savedPath);
        string[] staging = Directory.GetFiles(ProjectSettings.GlobalizePath(SavesFolder), "*.tmp");
        var failures = new List<string>();
        if (error == Error.Ok)
        {
            failures.Add($"ResourceSaver.Save({savedPath}) returned Ok");
        }

        if (File.Exists(ProjectSettings.GlobalizePath(savedPath)))
        {
            failures.Add($"{savedPath} exists");
        }

        if (staging.Length > 0)
        {
            failures.Add($"staging files left in {SavesFolder}: [{string.Join(", ", staging)}]");
        }

        return string.Join("; ", failures);
    }

    // Saves the resource, then parses the saved file; the failure, or "" with the parsed document.
    private static string SaveAndParse(Resource resource, string path, XscnDocumentKind kind, out XscnDocument? document)
    {
        document = null;
        string text = Encoding.UTF8.GetString(SaveAndRead(resource, path));
        XscnParseResult result = XscnParser.Parse(text, kind, XscnMetadata.Value);
        if (result.HasErrors)
        {
            return $"{path} does not parse: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}"));
        }

        document = result.Document;
        return "";
    }

    private static byte[] SaveAndRead(Resource resource, string path)
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        Error error = ResourceSaver.Save(resource, path);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"ResourceSaver.Save({path}) returned {error}");
        }

        return File.ReadAllBytes(ProjectSettings.GlobalizePath(path));
    }

    // Each node's name and class, root first, then each child's subtree in child order.
    private static List<string> PreOrder(PackedScene scene)
    {
        Node root = scene.Instantiate();
        try
        {
            var nodes = new List<string>();
            var pending = new Stack<Node>([root]);
            while (pending.TryPop(out Node? node))
            {
                nodes.Add($"{node.Name}:{node.GetClass()}");
                for (int child = node.GetChildCount() - 1; child >= 0; child--)
                {
                    pending.Push(node.GetChild(child));
                }
            }

            return nodes;
        }
        finally
        {
            root.Free();
        }
    }

    private static int Occurrences(string text, string value) =>
        (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private static string ExtensionsSplitByType()
    {
        (string Type, string[] Has, string[] Lacks)[] expected =
        [
            ("PackedScene", ["xscn"], ["xres"]),
            ("StyleBox", ["xres"], ["xscn"]),
            ("Resource", ["xscn", "xres"], []),
            ("GDExtension", [], ["xscn", "xres"]),
        ];
        var failures = new List<string>();
        foreach ((string type, string[] has, string[] lacks) in expected)
        {
            string[] extensions = ResourceLoader.GetRecognizedExtensionsForType(type);
            string list = string.Join(", ", extensions);
            failures.AddRange(has.Where(e => !extensions.Contains(e)).Select(e => $"{type}'s extensions [{list}] lack {e}"));
            failures.AddRange(lacks.Where(extensions.Contains).Select(e => $"{type}'s extensions [{list}] contain {e}"));
        }

        return string.Join("; ", failures);
    }

    private static string ReportsDependencies()
    {
        (string File, string[] Expected)[] cases = [(HolderPath, [ThemePath, BoxPath]), (ParentPath, [ChildScenePath])];
        var failures = new List<string>();
        foreach ((string file, string[] expected) in cases)
        {
            string[] dependencies = ResourceLoader.GetDependencies(file);
            string list = string.Join(", ", dependencies);
            if (dependencies.Length != expected.Length)
            {
                failures.Add($"{file} has {dependencies.Length} dependencies [{list}], not {expected.Length}");
            }

            failures.AddRange(
                expected.Where(p => !dependencies.Any(d => IsDependencyEntry(d, p))).Select(p => $"{file}'s dependencies [{list}] do not list {p}")
            );
        }

        failures.Add(UidEntriesFailure());
        return string.Join("; ", failures.Where(f => f.Length > 0));
    }

    // The text loader's uid entries with and without the type (resource_format_text.cpp:967-975).
    private static string UidEntriesFailure()
    {
        const string path = OutFolder + "uids.xscn";
        DeleteFolder(OutFolder);
        try
        {
            WriteHolderWithThemeUid(path, withValueReference: false);
            using var loader = new XscnFormatLoader();
            string uid = UidTextOf(ThemePath);
            (bool AddTypes, string Expected)[] cases = [(true, $"{uid}::Theme::{ThemePath}"), (false, $"{uid}::::{ThemePath}")];
            return string.Join(
                "; ",
                cases
                    .Select(c => (c.AddTypes, c.Expected, Entries: loader._GetDependencies(path, c.AddTypes)))
                    .Where(r => !r.Entries.Contains(r.Expected))
                    .Select(r => $"_GetDependencies({path}, {r.AddTypes}) gave [{string.Join(", ", r.Entries)}], with no \"{r.Expected}\"")
            );
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string UidTextOf(string path) => ResourceUid.IdToText(ResourceLoader.GetResourceUid(path));

    // holder.xscn with an x:Uids entry giving theme.xres its uid, and, when asked, a metadata Resource() reference carrying
    // that uid. An untyped reference shares the theme's ext_resource and makes its type Resource.
    private static void WriteHolderWithThemeUid(string path, bool withValueReference)
    {
        string holder = File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath));
        int afterRootTag = holder.IndexOf(">\n", StringComparison.Ordinal) + 2;
        string uid = UidTextOf(ThemePath);
        string inserted =
            $"  <x:Uids>\n    <x:Uid Path=\"{ThemePath}\" Value=\"{uid}\" />\n  </x:Uids>\n"
            + (withValueReference ? $"  <x:Prop Name=\"metadata/ref\" Value='Resource(\"{uid}\", \"{ThemePath}\")' />\n" : "");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        File.WriteAllText(ProjectSettings.GlobalizePath(path), holder.Insert(afterRootTag, inserted));
    }

    private static XscnParseResult ParseFile(string path) =>
        XscnParser.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(path)), XscnDocumentKind.Scene, XscnMetadata.Value);

    private static GodotPathRef? RootReference(XscnDocument document) =>
        (document.Root as XscnNodeElement)?.Properties.FirstOrDefault(p => p.GodotName == "metadata/ref")?.Value
            is XscnScalar { Value: GodotPathRef reference }
            ? reference
            : null;

    // A replaced dependency takes its new file's uid, in x:Uids and in value text (resource_format_text.cpp:1076-1080).
    private static string UidRenameFailure(XscnFormatLoader loader)
    {
        const string path = OutFolder + "uids.xscn";
        WriteHolderWithThemeUid(path, withValueReference: true);
        Error error = loader._RenameDependencies(path, new Godot.Collections.Dictionary { [ThemePath] = BoxPath });
        XscnParseResult parsed = ParseFile(path);
        if (error != Error.Ok || parsed.HasErrors)
        {
            return $"renaming {ThemePath} to {BoxPath} in {path} returned {error}, and the file parses: {!parsed.HasErrors}";
        }

        string expected = $"{BoxPath}={UidTextOf(BoxPath)}";
        string table = string.Join(", ", parsed.Document.Uids.Select(u => $"{u.Path}={u.Uid}"));
        string reference = RootReference(parsed.Document) is { } found ? $"{found.Path}={found.Uid}" : "(none)";
        string[] failures =
        [
            table == expected ? "" : $"the renamed x:Uids table is [{table}], not [{expected}]",
            reference == expected ? "" : $"the renamed metadata reference is {reference}, not {expected}",
        ];
        return string.Join("; ", failures.Where(f => f.Length > 0));
    }

    // A path written relative to the file is resolved against the file's folder, mapped, and written relative again as
    // path_to_file gives it (resource_format_text.cpp:1060-1073, ustring.cpp:4839-4917): "./" for the same folder,
    // "../" to climb.
    private static string RelativeRenameFailure(XscnFormatLoader loader)
    {
        (string File, string Written, string OldPath, string NewPath, string Expected)[] cases =
        [
            (OutFolder + "relative.xscn", "theme.xres", OutFolder + "theme.xres", OutFolder + "moved.xres", "./moved.xres"),
            (OutFolder + "scenes/relative.xscn", "theme.xres", OutFolder + "scenes/theme.xres", OutFolder + "art/theme.xres", "../art/theme.xres"),
            (OutFolder + "scenes/shared.xscn", "../shared/x.xres", OutFolder + "shared/x.xres", OutFolder + "shared/y.xres", "../shared/y.xres"),
        ];
        return string.Join(
            "; ",
            cases
                .Select(c => RelativeCaseFailure(loader, c.File, c.Written, new Godot.Collections.Dictionary { [c.OldPath] = c.NewPath }, c.Expected))
                .Where(f => f.Length > 0)
        );
    }

    // From the project root, path_to_file climbs above res://; the spelling falls back to the absolute path there.
    private static string RelativeSpellingStaysInTheProject()
    {
        const string target = OutFolder + "sub/x.xres";
        (string Folder, string Expected)[] cases = [("res://", target), (OutFolder + "a/", "../sub/x.xres")];
        return string.Join(
            "; ",
            cases
                .Select(c => (c.Folder, c.Expected, Spelled: XscnLoading.RelativeSpelling(c.Folder, target)))
                .Where(r => r.Spelled != r.Expected)
                .Select(r => $"from {r.Folder}, {target} is spelled \"{r.Spelled}\", not \"{r.Expected}\"")
        );
    }

    // An instanced scene written relative to the file is an ext_resource like any other, so it follows a move too.
    private static string RelativeInstanceFailure(XscnFormatLoader loader)
    {
        const string path = OutFolder + "a/main.xscn";
        const string expected = "Scene=\"../b/child.xscn\"";
        string folder = ProjectSettings.GlobalizePath(OutFolder + "a/");
        Directory.CreateDirectory(folder);
        File.Copy(ProjectSettings.GlobalizePath(ChildScenePath), Path.Combine(folder, "child.xscn"), overwrite: true);
        File.WriteAllText(
            ProjectSettings.GlobalizePath(path),
            "<Node xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
                + "  <x:Instance Scene=\"child.xscn\" Name=\"C\" />\n"
                + "</Node>\n"
        );
        var renames = new Godot.Collections.Dictionary { [OutFolder + "a/child.xscn"] = OutFolder + "b/child.xscn" };
        Error error = loader._RenameDependencies(path, renames);
        string text = File.ReadAllText(ProjectSettings.GlobalizePath(path));
        if (error != Error.Ok || ParseFile(path).HasErrors)
        {
            return $"the relative instance rename in {path} returned {error}, and the file parses: {!ParseFile(path).HasErrors}";
        }

        return text.Contains(expected, StringComparison.Ordinal) ? "" : $"{path} does not hold {expected} after the rename:\n{text}";
    }

    private static string RelativeCaseFailure(
        XscnFormatLoader loader,
        string path,
        string written,
        Godot.Collections.Dictionary renames,
        string expected
    )
    {
        string folder = ProjectSettings.GlobalizePath(path.GetBaseDir());
        Directory.CreateDirectory(folder);
        File.Copy(ProjectSettings.GlobalizePath(ThemePath), Path.Combine(folder, "theme.xres"), overwrite: true);
        File.WriteAllText(
            ProjectSettings.GlobalizePath(path),
            "<Node xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
                + $"  <x:Prop Name=\"metadata/ref\" Value='Resource(\"{written}\")' />\n"
                + "</Node>\n"
        );
        Error error = loader._RenameDependencies(path, renames);
        XscnParseResult parsed = ParseFile(path);
        if (error != Error.Ok || parsed.HasErrors)
        {
            return $"the relative rename in {path} returned {error}, and the file parses: {!parsed.HasErrors}";
        }

        string? renamed = RootReference(parsed.Document)?.Path;
        return renamed == expected ? "" : $"{path}'s reference \"{written}\" reads \"{renamed}\" after the rename, not \"{expected}\"";
    }

    // The text loader's entry without types: the path, or the uid text, an empty type slot and the path.
    private static bool IsDependencyEntry(string entry, string path) =>
        entry == path
        || (
            entry.StartsWith("uid://", StringComparison.Ordinal)
            && entry.EndsWith("::::" + path, StringComparison.Ordinal)
            && entry.IndexOf("::", StringComparison.Ordinal) == entry.Length - path.Length - 4
        );

    private static string RenamesDependencies()
    {
        const string outPath = OutFolder + "holder.xscn";
        const string movedPath = CasesPath + "moved.xres";
        DeleteFolder(OutFolder);
        try
        {
            string outFile = ProjectSettings.GlobalizePath(outPath);
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
            File.WriteAllText(outFile, File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath)));
            using var loader = new XscnFormatLoader();
            Error error = loader._RenameDependencies(outPath, new Godot.Collections.Dictionary { [ThemePath] = movedPath });
            if (error != Error.Ok)
            {
                return $"_RenameDependencies({outPath}) returned {error}";
            }

            string renamed = File.ReadAllText(outFile);
            XscnParseResult parsed = XscnParser.Parse(renamed, XscnDocumentKind.Scene, XscnMetadata.Value);
            string[] failures =
            [
                renamed.Contains(movedPath, StringComparison.Ordinal) ? "" : $"the renamed file does not name {movedPath}",
                renamed.Contains(ThemePath, StringComparison.Ordinal) ? $"the renamed file still names {ThemePath}" : "",
                renamed.Contains(BoxPath, StringComparison.Ordinal) ? "" : $"the renamed file no longer names {BoxPath}",
                parsed.HasErrors ? "the renamed file does not parse" : "",
                UnmatchedRenameFailure(loader, outPath, outFile),
                UidRenameFailure(loader),
                RelativeRenameFailure(loader),
                RelativeInstanceFailure(loader),
            ];
            return string.Join("; ", failures.Where(f => f.Length > 0)) + (failures.Any(f => f.Length > 0) ? $":\n{renamed}" : "");
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string UnmatchedRenameFailure(XscnFormatLoader loader, string path, string file)
    {
        byte[] before = File.ReadAllBytes(file);
        var renames = new Godot.Collections.Dictionary { ["res://nowhere/a.xres"] = "res://nowhere/b.xres" };
        Error error = loader._RenameDependencies(path, renames);
        if (error != Error.Ok)
        {
            return $"a rename naming nothing in {path} returned {error}";
        }

        return File.ReadAllBytes(file).SequenceEqual(before) ? "" : $"a rename naming nothing in {path} changed its bytes";
    }

    private async Task<string> ThreadedLoad(string path, Func<Resource, int, string> resultFailure)
    {
        int callsBefore = XscnLoading.LoadCallCount;
        Error request = ResourceLoader.LoadThreadedRequest(path, "", useSubThreads: true, ResourceLoader.CacheMode.IgnoreDeep);
        if (request != Error.Ok)
        {
            return $"LoadThreadedRequest({path}) returned {request}";
        }

        for (int frame = 0; frame < ThreadedLoadFrameGuard; frame++)
        {
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                return resultFailure(ResourceLoader.LoadThreadedGet(path), XscnLoading.LoadCallCount - callsBefore);
            }

            if (status != ResourceLoader.ThreadLoadStatus.InProgress)
            {
                return $"the threaded load of {path} ended {status}";
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return $"hung: the threaded load of {path} was not done after {ThreadedLoadFrameGuard} frames";
    }

    private static string ThreadedResultFailure(Resource loaded, int calls)
    {
        if (loaded is not PackedScene scene)
        {
            return $"LoadThreadedGet returned {loaded?.GetClass() ?? "null"}, not a PackedScene";
        }

        if (calls < 2)
        {
            return $"the custom loader was called {calls} times, not for both parent.xscn and child.xscn";
        }

        Node root = scene.Instantiate();
        bool hasInner = root.GetNodeOrNull("Inst/Inner") is not null;
        root.Free();
        return hasInner ? "" : "the threaded load's scene has no Inst/Inner";
    }

    // The holder, its theme and its stylebox each load through the custom loader on Godot's loader threads.
    private static string ThreadedHolderFailure(Resource loaded, int calls)
    {
        if (loaded is not PackedScene scene)
        {
            return $"LoadThreadedGet returned {loaded?.GetClass() ?? "null"}, not a PackedScene";
        }

        const int files = 3;
        string[] failures =
        [
            calls >= files ? "" : $"the custom loader was called {calls} times, not for holder.xscn, theme.xres and box.xres",
            .. ExternalPathFailures(scene, ThemePath),
        ];
        return string.Join("; ", failures.Where(f => f.Length > 0));
    }
}
