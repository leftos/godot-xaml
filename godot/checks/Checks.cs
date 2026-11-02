using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodotXaml.Addon;
using GodotXaml.Core.Metadata;
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
        ];
        int total = checks.Length + 1;
        XscnLoadRoute.RecordTempPaths = true;
        tempPathsBefore = XscnLoadRoute.TempPaths.Count;
        int passed = 0;
        try
        {
            foreach ((string id, Func<string> run) in checks)
            {
                passed += Report(id, run) ? 1 : 0;
            }

            string threadedLoadFailure = await FailureOf(ThreadedLoad);
            passed += Report("C18", () => threadedLoadFailure) ? 1 : 0;
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
        int callsBefore = XscnFormatLoader.LoadCallCount;
        Resource loaded = ResourceLoader.Load(BrokenScenePath, "", ResourceLoader.CacheMode.Ignore);
        int calls = XscnFormatLoader.LoadCallCount - callsBefore;
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
        if (saveError != Error.Unavailable)
        {
            return $"ResourceSaver.Save({ProbeSavePath}) returned {saveError}, not Unavailable (custom saver calls: {calls})";
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

    // Godot binds no ResourceLoader.get_resource_type for scripts (core/core_bind.cpp), so this asks the loader directly;
    // that the engine routes the question to it is proved by the editor checks, not here.
    private static string XresReportsItsRootClass()
    {
        using var loader = new XscnFormatLoader();
        (string Path, string Type)[] expected =
        [
            (BoxPath, "StyleBoxFlat"),
            (CasesPath + "notxml.xres", "Resource"),
            (CasesPath + "scene.xscn", "PackedScene"),
        ];
        return string.Join(
            "; ",
            expected
                .Select(e => (e.Path, e.Type, Reported: loader._GetResourceType(e.Path)))
                .Where(e => e.Reported != e.Type)
                .Select(e => $"{e.Path} reports type \"{e.Reported}\", not {e.Type}")
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
        int callsBefore = XscnFormatLoader.LoadCallCount;
        Resource loaded = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore);
        int calls = XscnFormatLoader.LoadCallCount - callsBefore;
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

    private async Task<string> ThreadedLoad()
    {
        const string path = CasesPath + "parent.xscn";
        int callsBefore = XscnFormatLoader.LoadCallCount;
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
                return ThreadedResultFailure(ResourceLoader.LoadThreadedGet(path), XscnFormatLoader.LoadCallCount - callsBefore);
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
}
