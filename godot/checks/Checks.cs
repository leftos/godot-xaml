using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GodotXaml.Addon;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
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
    private const string ContainedPath = CasesPath + "contained.xscn";
    private const string ContainerPath = CasesPath + "container.xres";
    private const string SavesFolder = "user://gx-saves/";
    private const string OutFolder = "res://checks/out/";
    private const string HeldScriptPath = CasesPath + "held.gd";
    private const string ScriptedCsPath = CasesPath + "scripted_cs.xres";
    private const string ScriptedGdPath = CasesPath + "scripted_gd.xres";
    private const string RelativeCasesPath = CasesPath + "relative/";
    private const string RelativeHolderPath = RelativeCasesPath + "holder.xscn";
    private const string AbstractSubResource =
        "  <CollisionShape2D Name=\"Abstract\">\n"
        + "    <CollisionShape2D.Shape>\n"
        + "      <Shape2D />\n"
        + "    </CollisionShape2D.Shape>\n"
        + "  </CollisionShape2D>\n";

    // Two users of one material, so the conversion keeps it in x:Resources rather than inlining it.
    private const string KeysScene =
        "<Node2D xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Resources>\n"
        + "    <CanvasItemMaterial x:Key=\"HandKey\" LightMode=\"Unshaded\" />\n"
        + "  </x:Resources>\n"
        + "  <Sprite2D Name=\"One\" Material=\"{HandKey}\" />\n"
        + "  <Sprite2D Name=\"Two\" Material=\"{HandKey}\" />\n"
        + "</Node2D>\n";

    // C54-C56's scene A: a red box both its panels use, so it is an x:Resources entry.
    private const string ForeignAScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"A\">\n"
        + "  <x:Resources>\n"
        + "    <StyleBoxFlat x:Key=\"StyleBoxFlat1\" BgColor=\"1, 0, 0, 1\" />\n"
        + "  </x:Resources>\n"
        + "  <Panel Name=\"P1\" ThemeOverrideStyles.panel=\"{StyleBoxFlat1}\" />\n"
        + "  <Panel Name=\"P2\" ThemeOverrideStyles.panel=\"{StyleBoxFlat1}\" />\n"
        + "</Control>\n";

    private const string OwnBoxComment = "<!-- B's own blue box -->";

    // C54-C56's scene B: its own blue box under the same key, used by the two panels below F1 and F2, which get A's box.
    private const string ForeignBScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"B\">\n"
        + "  <x:Resources>\n"
        + "    "
        + OwnBoxComment
        + "\n"
        + "    <StyleBoxFlat x:Key=\"StyleBoxFlat1\" BgColor=\"0, 0, 1, 1\" />\n"
        + "  </x:Resources>\n"
        + "  <Panel Name=\"F1\" />\n"
        + "  <Panel Name=\"F2\" />\n"
        + "  <Panel Name=\"Own1\" ThemeOverrideStyles.panel=\"{StyleBoxFlat1}\" />\n"
        + "  <Panel Name=\"Own2\" ThemeOverrideStyles.panel=\"{StyleBoxFlat1}\" />\n"
        + "</Control>\n";

    private const string OwnKeyUse = "ThemeOverrideStyles.panel=\"{StyleBoxFlat1}\"";

    private const string ARedBoxComment = "<!-- A's own red box -->";

    // C61's scene A: C54's A with a comment above its box.
    private static readonly string ForeignACommentedScene = ForeignAScene.Replace(
        "  <x:Resources>\n",
        "  <x:Resources>\n    " + ARedBoxComment + "\n",
        StringComparison.Ordinal
    );

    // C62's first file: a shared box and an inline one on the Button, written by hand, so no node carries an id yet.
    private const string SaveAsFirstScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Resources>\n"
        + "    <StyleBoxFlat x:Key=\"Shared\" BgColor=\"1, 0, 0, 1\" />\n"
        + "  </x:Resources>\n"
        + "  <Button Name=\"Button\" ThemeOverrideStyles.normal=\"{Shared}\">\n"
        + "    <Button.ThemeOverrideStyles.hover>\n"
        + "      <StyleBoxFlat BgColor=\"0, 1, 0, 1\" />\n"
        + "    </Button.ThemeOverrideStyles.hover>\n"
        + "  </Button>\n"
        + "  <Panel Name=\"Other\" ThemeOverrideStyles.panel=\"{Shared}\" />\n"
        + "</Control>\n";

    // C63's scene: an unknown class parses with a GX0107 warning and nothing else.
    private const string WarnedScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <NoSuchGodotClass Name=\"Unknown\" />\n"
        + "</Control>\n";

    // C64's scene: the same warning beside a parse error, a node without a Name (GX0104).
    private const string FailedParseScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <NoSuchGodotClass Name=\"Unknown\" />\n"
        + "  <Panel />\n"
        + "</Control>\n";

    private static readonly string[] ResourceTypesToHandle = ["StyleBox", "Theme"];

    // A hang guard, not a time budget: the threaded load of two small scenes takes a few frames, and checks.ps1 quits the
    // run after 600.
    private const int ThreadedLoadFrameGuard = 300;

    // The garbage collections C45 allows a dropped scene's sub-resource to leave the cache in, each followed by a frame.
    private const int ContainerReleaseGcPasses = 3;

    // The colour of C53's box held only inside an array, which its fixture gives the other box in a plain property.
    private static readonly Color ListedBoxColor = new(0, 1, 0);

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
            ("C33", SaveTakesOverSubResourcePaths),
            ("C34", LoadRegistersSubResources),
            ("C35", ReplaceKeepsALiveSubResource),
            ("C36", ReplaceKeepsALiveSubResourceInATscn),
            ("C37", SaveDoesNotTakeOverWithoutTheFlag),
            ("C38", ReplaceRestoresAfterAFailedLoad),
            ("C41", SaveLeavesANonStoredResourceAlone),
            ("C43", RenameKeepsTheLiveSubResource),
            ("C44", SaveOverAnExistingFileTakesOverTheBox),
            ("C46", RelativeInstanceLoads),
            ("C47", RelativeResourceLoads),
            ("C48", HeadlessSaveKeepsUids),
            ("C49", SetUidNeedsAFile),
            ("C50", HeadlessSaveKeepsAnUncachedUid),
            ("C51", RemappedFileResolvesAgainstItsOriginalPath),
            ("C52", EditingAKeyedResourceKeepsItsKey),
            ("C54", ForeignSubResourceDoesNotTakeAKey),
            ("C55", ForeignSubResourceDoesNotTakeAnEditedKey),
            ("C56", OneForeignUserKeepsTheOwnComment),
            ("C58", NonCanonicalPathKeepsTheKey),
            ("C59", ScriptedResourceKeepsItsWrapper),
            ("C60", ScriptHandsItselfToCSharp),
            ("C61", ForeignOwnerKeepsItsKeyAfterTheTakeover),
            ("C62", SaveAsFromALoadedFileTakesOverTheBoxes),
            ("C63", CleanParseWarningsArePushed),
            ("C64", FailedParsePushesWarningsAsWarnings),
            ("C65", FailedLoadPrunesOldKeptFiles),
            ("C66", KnownEngineClassIsNotWarned),
            ("C67", XresReportsItsScriptClass),
            ("C68", XresScriptClassOffTheMainThread),
            ("C69", HandlesGlobalResourceClasses),
            // A temp folder in the shared /tmp let another account block or read every conversion (GXAML-149).
            ("C71", TempFolderIsPerUser),
        ];
        (string Id, Func<Task<string>> Run)[] frameChecks =
        [
            ("C18", () => ThreadedLoad(ParentPath, ThreadedResultFailure)),
            ("C31", () => ThreadedLoad(HolderPath, ThreadedHolderFailure)),
            ("C39", ReuseKeepsADroppedScenesSubResource),
            ("C40", ReuseKeepsADroppedScenesSubResourceInATscn),
            ("C42", DroppedSceneLeavesTheCache),
            ("C45", DroppedSceneReleasesContainerSubResources),
            ("C53", DroppedResourceReleasesContainerSubResources),
            ("C57", EngineDroppedSceneLeavesTheCache),
        ];
        DeleteFolder(SavesFolder);
        int total = checks.Length + frameChecks.Length + 1;
        XscnLoadRoute.RecordTempPaths = true;
        tempPathsBefore = XscnLoadRoute.TempPaths.Count;
        int passed = 0;
        try
        {
            foreach ((string id, Func<string> run) in checks)
            {
                passed += Report(id, run) ? 1 : 0;
            }

            // These wait for frames, which the checks above never see: they all run in one.
            foreach ((string id, Func<Task<string>> run) in frameChecks)
            {
                string failure = await FailureOf(run);
                passed += Report(id, () => failure) ? 1 : 0;
            }

            // Last, because a mounted pack cannot be unmounted and freezes res:// for the rest of the process.
            passed += Report("C70", RefreshSeesAMountedPacksClasses) ? 1 : 0;
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

    // A clean parse's warning reaches Godot's log as a warning: the unknown class makes the parse warn (GX0107) and nothing else.
    private static string CleanParseWarningsArePushed()
    {
        const string path = OutFolder + "warned.xscn";
        if (ParseSetupFailure(WarnedScene, expectError: false) is { } setup)
        {
            return setup;
        }

        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), WarnedScene);
            (Resource? loaded, WarningLog log) = LoadLogged(path);
            GC.KeepAlive(loaded);
            return Logged(log.Entries, Logger.ErrorType.Warning, "warned.xscn", XscnCodes.UnknownClass)
                ? ""
                : $"the load logged no warning naming warned.xscn and {XscnCodes.UnknownClass}: [{LogText(log.Entries)}]";
        }
        finally
        {
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            DeleteFolder(OutFolder);
        }
    }

    // A failed parse pushes its warning as a warning and its error as an error, and still fails the load.
    private static string FailedParsePushesWarningsAsWarnings()
    {
        const string path = OutFolder + "failed.xscn";
        if (ParseSetupFailure(FailedParseScene, expectError: true) is { } setup)
        {
            return setup;
        }

        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), FailedParseScene);
            (Resource? loaded, WarningLog log) = LoadLogged(path);

            // The fixture's error is the node without a Name, and the load returns nothing for either of them.
            return Failures(
                loaded is null ? "" : $"loading {path} returned a {loaded.GetClass()}, not null",
                Logged(log.Entries, Logger.ErrorType.Error, XscnCodes.MissingName) ? "" : $"the log holds no {XscnCodes.MissingName} error",
                Logged(log.Entries, Logger.ErrorType.Warning, XscnCodes.UnknownClass) ? "" : $"the log holds no {XscnCodes.UnknownClass} warning"
            );
        }
        finally
        {
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            DeleteFolder(OutFolder);
        }
    }

    // A failed load that keeps its temp file prunes the folder: an old kept file goes, a recent one stays, and the error still
    // names this load's own file.
    private static string FailedLoadPrunesOldKeptFiles()
    {
        const string copyPath = OutFolder + "prune.xscn";
        string loaderTempFolder = AddonTempFolder.Get();
        string oldFile = Path.Combine(loaderTempFolder, $"{Guid.NewGuid():N}.tscn");
        string recentFile = Path.Combine(loaderTempFolder, $"{Guid.NewGuid():N}.tres");
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            File.WriteAllText(oldFile, "");
            File.WriteAllText(recentFile, "");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(2));
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow - TimeSpan.FromHours(1));
            DeleteFolder(OutFolder);
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
            string holder = File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath));
            File.WriteAllText(
                ProjectSettings.GlobalizePath(copyPath),
                holder.Replace("</Control>", AbstractSubResource + "</Control>", StringComparison.Ordinal)
            );
            WarningLog log = LoadLogged(copyPath).Log;
            string[] kept = [.. XscnLoadRoute.TempPaths.Skip(tempPathsBeforeLoad).Where(File.Exists)];
            return Failures(
                File.Exists(oldFile) ? $"the file kept two days was not pruned: {oldFile}" : "",
                File.Exists(recentFile) ? "" : $"the file kept an hour was pruned: {recentFile}",
                kept.Length > 0 ? "" : "the failed load kept no temp file",
                kept.Any(file => Logged(log.Entries, Logger.ErrorType.Error, file)) ? "" : $"no error names the kept file [{string.Join(", ", kept)}]"
            );
        }
        finally
        {
            File.Delete(recentFile);
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            if (File.Exists(oldFile))
            {
                File.Delete(oldFile);
            }

            DeleteFolder(OutFolder);
        }
    }

    // A class the running engine has is never warning material: the bundled metadata can lack a class this engine registered,
    // and the file still loads through it.
    private static string KnownEngineClassIsNotWarned()
    {
        const string path = OutFolder + "known.xscn";
        var metadata = GodotMetadata.LoadBundled("4.7");
        string? engineClass = ClassDB
            .GetClassList()
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(name =>
                metadata.FindClassByCSharpName(name) is null
                && XscnParser.Parse(KnownScene(name), XscnDocumentKind.Scene, metadata).Diagnostics.Any(d => d.Code == XscnCodes.UnknownClass)
            );
        if (engineClass is null)
        {
            return "setup: every ClassDB class is in the bundled metadata";
        }

        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), KnownScene(engineClass));
            (Resource? loaded, WarningLog log) = LoadLogged(path);
            GC.KeepAlive(loaded);
            return LoggedAny(log.Entries, "known.xscn", XscnCodes.UnknownClass)
                ? $"the load warned about {engineClass}, which this engine has: [{LogText(log.Entries)}]"
                : "";
        }
        finally
        {
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            DeleteFolder(OutFolder);
        }
    }

    // The scene C66 loads: a root node holding one child of the named class.
    private static string KnownScene(string className) => $"<Node Name=\"Root\">\n  <{className} Name=\"Known\" />\n</Node>\n";

    // The setup a warning check needs: the parse reaches Godot's log at all, with the diagnostics its fixture means it to have.
    private static string? ParseSetupFailure(string text, bool expectError)
    {
        XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, GodotMetadata.LoadBundled("4.7"));
        bool warned = result.Diagnostics.Any(d => d.Code == XscnCodes.UnknownClass && d.Severity == XscnSeverity.Warning);
        bool errored = result.Diagnostics.Any(d => d.Severity == XscnSeverity.Error);
        if (warned && errored == expectError)
        {
            return null;
        }

        string codes = string.Join(", ", result.Diagnostics.Select(d => $"{d.Code} ({d.Severity})"));
        string expected = expectError ? "an error" : "no error";
        return $"setup: the parse gave [{codes}], not a {XscnCodes.UnknownClass} warning and {expected}";
    }

    // Loads the file with a logger added around the load, and hands back what it returned and what it logged.
    private static (Resource? Loaded, WarningLog Log) LoadLogged(string path)
    {
        var log = new WarningLog();
        OS.AddLogger(log);
        try
        {
            return (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore), log);
        }
        finally
        {
            OS.RemoveLogger(log);
        }
    }

    // Whether the log holds an entry of this severity whose text names every one of the given strings.
    private static bool Logged(IReadOnlyList<(Logger.ErrorType Type, string Text)> entries, Logger.ErrorType type, params string[] names) =>
        entries.Any(entry => entry.Type == type && names.All(name => entry.Text.Contains(name, StringComparison.Ordinal)));

    // Whether the log holds any entry, whatever its severity, whose text names every one of the given strings.
    private static bool LoggedAny(IReadOnlyList<(Logger.ErrorType Type, string Text)> entries, params string[] names) =>
        entries.Any(entry => names.All(name => entry.Text.Contains(name, StringComparison.Ordinal)));

    // The log's entries as text, each with the severity it was logged as, for a failure message.
    private static string LogText(IReadOnlyList<(Logger.ErrorType Type, string Text)> entries) =>
        string.Join(", ", entries.Select(entry => $"{entry.Type}: {entry.Text}"));

    private static void DeleteKeptTempFiles(int countBefore)
    {
        foreach (string kept in XscnLoadRoute.TempPaths.Skip(countBefore).Where(File.Exists))
        {
            File.Delete(kept);
        }
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

    // The editor's scan and Quick Open ask a loader for a file's script class, which a .xres carries only as its root's
    // Script attribute.
    private static string XresReportsItsScriptClass()
    {
        using var loader = new XresFormatLoader();
        (string Path, string Expected)[] expected =
        [
            (ScriptedCsPath, "CheckedResource"),
            (CasesPath + "named_gd.xres", "NamedCheckedResource"),
            (ScriptedGdPath, ""),
            (BoxPath, ""),
        ];
        return string.Join(
            "; ",
            expected
                .Select(e => (e.Path, e.Expected, Reported: loader._GetResourceScriptClass(e.Path)))
                .Where(e => e.Reported != e.Expected)
                .Select(e => $"the loader reports script class \"{e.Reported}\" for {e.Path}, not \"{e.Expected}\"")
        );
    }

    // The editor asks it from its scan thread, so the answer comes from the snapshot rather than the main thread's live
    // global class list.
    private static string XresScriptClassOffTheMainThread()
    {
        using var loader = new XresFormatLoader();
        string reported = Task.Run(() => loader._GetResourceScriptClass(ScriptedCsPath)).GetAwaiter().GetResult();
        return reported == "CheckedResource"
            ? ""
            : $"off the main thread the loader reports script class \"{reported}\" for {ScriptedCsPath}, not \"CheckedResource\"";
    }

    // A global class whose native base is a Resource is a type an .xres can hold, as the text loader accepts any type for a
    // .tres. The engine asks this when a caller passes the class as a type hint.
    private static string HandlesGlobalResourceClasses()
    {
        using var loader = new XresFormatLoader();
        (string Type, bool Expected)[] expected =
        [
            ("CheckedResource", true),
            ("DerivedCheckedResource", true),
            ("NamedCheckedResource", true),
            ("XresFormatLoader", false),
            ("PackedScene", false),
            ("NoSuchGodotClass", false),
            ("StyleBoxFlat", true),
        ];
        var failures = new List<string>(
            expected
                .Select(e => (e.Type, e.Expected, Handled: loader._HandlesType(e.Type)))
                .Where(e => e.Handled != e.Expected)
                .Select(e => $"_HandlesType(\"{e.Type}\") is {e.Handled}, not {e.Expected}")
        );
        string[] extensions = ResourceLoader.GetRecognizedExtensionsForType("CheckedResource");
        if (!extensions.Contains("xres"))
        {
            failures.Add($"the loaders' extensions for CheckedResource [{string.Join(", ", extensions)}] lack xres");
        }

        if (!ResourceLoader.Exists(ScriptedCsPath, "CheckedResource"))
        {
            failures.Add($"ResourceLoader.Exists({ScriptedCsPath}, \"CheckedResource\") is false");
        }

        if (ResourceLoader.Load(ScriptedCsPath, "CheckedResource", ResourceLoader.CacheMode.Ignore) is not CheckedResource resource)
        {
            failures.Add($"loading {ScriptedCsPath} as \"CheckedResource\" did not return a CheckedResource");
        }
        else if (resource.Count != 7)
        {
            failures.Add($"the loaded CheckedResource's Count is {resource.Count}, not 7");
        }

        const string namedPath = CasesPath + "named_gd.xres";
        if (ResourceLoader.Load(namedPath, "NamedCheckedResource", ResourceLoader.CacheMode.Ignore) is not Resource named)
        {
            failures.Add($"loading {namedPath} as \"NamedCheckedResource\" did not return a resource");
        }
        else if (named.Get("count").AsInt64() != 3)
        {
            failures.Add($"the loaded {namedPath} has count {named.Get("count")}, not 3");
        }

        return string.Join("; ", failures);
    }

    // Mounting a pack refreshes Godot's class list but re-creates no loader, so only the explicit refresh shows its classes.
    private static string RefreshSeesAMountedPacksClasses()
    {
        const string dlcScriptPath = "res://checks/dlc/dlc_checked.gd";
        const string classCachePath = "res://.godot/global_script_class_cache.cfg";
        const string dlcClass = "DlcCheckedResource";
        string xresFolder = ProjectSettings.GlobalizePath(OutFolder + "dlc/");
        string xresPath = OutFolder + "dlc/dlc.xres";

        // A loader made after the mount would rebuild the snapshot in its constructor and hide the fault.
        using var loader = new XresFormatLoader();
        string temp = Path.Combine(Path.GetTempPath(), "gx-check-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(xresFolder);
        try
        {
            string scriptFile = Path.Combine(temp, "dlc_checked.gd");
            File.WriteAllText(scriptFile, $"class_name {dlcClass}\nextends Resource\n\n@export var count: int = 0\n");
            File.WriteAllText(
                ProjectSettings.GlobalizePath(xresPath),
                $"<Resource xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Script=\"{dlcScriptPath}\" count=\"3\" />\n"
            );

            string cacheFile = Path.Combine(temp, "global_script_class_cache.cfg");
            string cacheFailure = WriteClassCacheWithDlc(classCachePath, cacheFile, dlcClass, dlcScriptPath);
            if (cacheFailure.Length > 0)
            {
                return cacheFailure;
            }

            string mountFailure = MountPack(Path.Combine(temp, "dlc.pck"), [(dlcScriptPath, scriptFile), (classCachePath, cacheFile)]);
            if (mountFailure.Length > 0)
            {
                return mountFailure;
            }

            string before = loader._GetResourceScriptClass(xresPath);
            if (before == dlcClass)
            {
                return "the snapshot already knew the pack's class before the refresh; the check does not reproduce GXAML-150";
            }

            if (before.Length > 0)
            {
                return $"before the refresh the loader reports script class \"{before}\" for {xresPath}, not \"\"";
            }

            XresFormatLoader.RefreshGlobalClasses();
            var failures = new List<string>();
            string after = loader._GetResourceScriptClass(xresPath);
            if (after != dlcClass)
            {
                failures.Add($"after the refresh the loader reports script class \"{after}\" for {xresPath}, not \"{dlcClass}\"");
            }

            if (!loader._HandlesType(dlcClass))
            {
                failures.Add($"after the refresh _HandlesType(\"{dlcClass}\") is false");
            }

            return string.Join("; ", failures);
        }
        finally
        {
            DeleteFolder(OutFolder);
            RemoveTempFolder(temp);
        }
    }

    // The pack's sources are scratch files outside the project, so a folder left behind is worth a warning, not a failure.
    private static void RemoveTempFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"C70 could not remove its temp folder {folder}: {exception.Message}");
        }
    }

    // Each packing call reports its own error, so a failure names the call that made it.
    private static string MountPack(string pck, (string Target, string Source)[] files)
    {
        using var packer = new PckPacker();
        (string Call, Func<Error> Run)[] packing =
        [
            ($"PckStart({pck})", () => packer.PckStart(pck)),
            .. files.Select(f => ($"AddFile({f.Target})", (Func<Error>)(() => packer.AddFile(f.Target, f.Source)))),
            ("Flush()", () => packer.Flush()),
        ];
        foreach ((string call, Func<Error> run) in packing)
        {
            Error error = run();
            if (error != Error.Ok)
            {
                return $"PCKPacker.{call} returned {error}";
            }
        }

        return ProjectSettings.LoadResourcePack(pck) ? "" : $"ProjectSettings.LoadResourcePack({pck}) returned false";
    }

    // The pack's copy of the class cache replaces the project's, so it carries every current class plus the pack's own, each
    // value with the variant type the existing entries use.
    private static string WriteClassCacheWithDlc(string classCachePath, string cacheFile, string dlcClass, string dlcScriptPath)
    {
        using var current = new ConfigFile();
        Error loaded = current.Load(classCachePath);
        if (loaded != Error.Ok)
        {
            return $"loading {classCachePath} returned {loaded}";
        }

        using Godot.Collections.Array list = current.GetValue("", "list", new Godot.Collections.Array()).AsGodotArray();
        if (list.Count == 0)
        {
            return $"{classCachePath} lists no classes";
        }

        using Godot.Collections.Dictionary sample = list[0].AsGodotDictionary();
        Variant Text(string key, string value) =>
            sample.TryGetValue(key, out Variant existing) && existing.VariantType == Variant.Type.StringName
                ? Variant.From(new StringName(value))
                : Variant.From(value);
        using var dlc = new Godot.Collections.Dictionary
        {
            ["class"] = Text("class", dlcClass),
            ["base"] = Text("base", "Resource"),
            ["language"] = Text("language", "GDScript"),
            ["path"] = Text("path", dlcScriptPath),
            ["is_abstract"] = false,
            ["is_tool"] = false,
            ["icon"] = Text("icon", ""),
        };
        list.Add(dlc);
        using var written = new ConfigFile();
        written.SetValue("", "list", list);
        Error saved = written.Save(cacheFile);
        return saved == Error.Ok ? "" : $"saving the pack's class cache to {cacheFile} returned {saved}";
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
        string folder = AddonTempFolder.Get();
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Failures([
            left.Length == 0 ? "" : $"temp files left behind: [{string.Join(", ", left)}]",
            .. recorded.Where(p => !p.StartsWith(folder, comparison)).Select(p => $"temp file {p} is not in the addon's temp folder {folder}"),
        ]);
    }

    private static string TempFolderIsPerUser()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string checkFolder = ProjectSettings.GlobalizePath("user://gx-tempfolder-check");
        try
        {
            return Failures([
                .. PrimaryFolderFailures(),
                UnusablePrimaryFailure(projectRoot, Path.Combine(checkFolder, "primary")),
                PrimaryInsideTheProjectFailure(projectRoot),
                NoPrimaryFailure(projectRoot),
                UsablePrimaryFailure(projectRoot, Path.Combine(checkFolder, "ok")),
                NoProjectFolderFailure(Path.Combine(checkFolder, "exported")),
                CachedFolderFailure(),
                RemovedPrimaryFailure(Path.Combine(checkFolder, "removed")),
                RemovedFallbackFailure(Path.Combine(checkFolder, "fallback-file")),
            ]);
        }
        finally
        {
            AddonTempFolder.ResetForChecks(null);
            if (Directory.Exists(checkFolder))
            {
                Directory.Delete(checkFolder, recursive: true);
            }
        }
    }

    // One row per input state of the rule: the platform, then XDG_CACHE_HOME and HOME (null when unset).
    private static IEnumerable<string> PrimaryFolderFailures()
    {
        (string Case, TempFolderPlatform Platform, string? Xdg, string? Home, string? Expected)[] cases =
        [
            ("Windows", TempFolderPlatform.Windows, null, null, Path.Combine("C:/T/", "godot-xaml")),
            ("Linux with XDG_CACHE_HOME", TempFolderPlatform.Linux, "/x/cache", "/home/u", "/x/cache/godot-xaml"),
            ("Linux with a relative XDG_CACHE_HOME", TempFolderPlatform.Linux, "rel/cache", "/home/u", "/home/u/.cache/godot-xaml"),
            ("Linux with HOME only", TempFolderPlatform.Linux, null, "/home/u", "/home/u/.cache/godot-xaml"),
            ("Linux with neither", TempFolderPlatform.Linux, null, null, null),
            ("Linux with a relative HOME", TempFolderPlatform.Linux, null, "relative", null),
            ("macOS with HOME", TempFolderPlatform.MacOS, null, "/Users/u", "/Users/u/Library/Caches/godot-xaml"),
            ("macOS without HOME", TempFolderPlatform.MacOS, null, null, null),
        ];
        foreach ((string name, TempFolderPlatform platform, string? xdg, string? home, string? expected) in cases)
        {
            string? actual = AddonTempFolder.PrimaryFolder(
                platform,
                variable =>
                    variable switch
                    {
                        "XDG_CACHE_HOME" => xdg,
                        "HOME" => home,
                        _ => null,
                    },
                "C:/T/"
            );
            if (actual != expected)
            {
                yield return $"{name}: the per-user folder is {actual ?? "null"}, not {expected ?? "null"}";
            }
        }
    }

    // A primary that is a file cannot be used: the resolver falls back to a fresh private folder and says why.
    private static string UnusablePrimaryFailure(string projectRoot, string primary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
        File.WriteAllText(primary, "");
        (string folder, string? reason) = AddonTempFolder.ResolveFrom(primary, projectRoot);
        try
        {
            return Failures(
                SameFolder(folder, primary) ? $"a file as the primary was used as the folder: {folder}" : "",
                Directory.Exists(folder) ? "" : $"the fallback for a file as the primary does not exist: {folder}",
                Path.IsPathFullyQualified(folder) ? "" : $"the fallback for a file as the primary is not absolute: {folder}",
                reason is not null ? "" : "a file as the primary fell back without a reason"
            );
        }
        finally
        {
            DeleteFallback(folder, primary);
            File.Delete(primary);
        }
    }

    // A folder inside the project would be imported and could ship; the resolver must not even create it.
    private static string PrimaryInsideTheProjectFailure(string projectRoot)
    {
        string inside = Path.Combine(projectRoot, "gx-temp-inside");
        (string folder, string? reason) = AddonTempFolder.ResolveFrom(inside, projectRoot);
        DeleteFallback(folder, inside);
        return Failures(
            reason is not null ? "" : $"a primary inside the project was used: {folder}",
            Directory.Exists(inside) || File.Exists(inside) ? $"a primary inside the project was created: {inside}" : ""
        );
    }

    private static string NoPrimaryFailure(string projectRoot)
    {
        (string folder, string? reason) = AddonTempFolder.ResolveFrom(null, projectRoot);
        DeleteFallback(folder, null);
        return reason is not null ? "" : $"no primary fell back to {folder} without a reason";
    }

    // The Linux branch runs only on Linux: there the folder must be the owner's alone.
    private static string UsablePrimaryFailure(string projectRoot, string primary)
    {
        (string folder, string? reason) = AddonTempFolder.ResolveFrom(primary, projectRoot);
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        string modeFailure = "";
        if (OperatingSystem.IsLinux() && SameFolder(folder, primary) && File.GetUnixFileMode(folder) != ownerOnly)
        {
            modeFailure = $"the usable primary's mode is {File.GetUnixFileMode(folder)}, not {ownerOnly}";
        }

        DeleteFallback(folder, primary);
        return Failures(
            SameFolder(folder, primary) ? "" : $"the usable primary {primary} was not used: {folder}",
            reason is null ? "" : $"the usable primary fell back: {reason}",
            modeFailure
        );
    }

    private static string CachedFolderFailure()
    {
        string first = AddonTempFolder.Get();
        string second = AddonTempFolder.Get();
        return Failures(
            first == second ? "" : $"the temp folder changed between two calls: {first}, then {second}",
            Directory.Exists(second) ? "" : $"the temp folder does not exist: {second}"
        );
    }

    // An exported game globalizes res:// to "", so the under-the-project rule cannot apply and must not throw.
    private static string NoProjectFolderFailure(string primary)
    {
        (string folder, string? reason) = AddonTempFolder.ResolveFrom(primary, "");
        DeleteFallback(folder, primary);
        return Failures(
            SameFolder(folder, primary) ? "" : $"with no project folder the usable primary {primary} was not used: {folder}",
            reason is null ? "" : $"with no project folder the usable primary fell back: {reason}"
        );
    }

    // A primary the OS removed is created again under the same path. Staged, so the folder other processes share stays.
    private static string RemovedPrimaryFailure(string primary)
    {
        AddonTempFolder.ResetForChecks(primary);
        string first = AddonTempFolder.Get();
        Directory.Delete(first, recursive: true);
        string second = AddonTempFolder.Get();
        return Failures(
            SameFolder(first, primary) ? "" : $"the staged primary {primary} was not used: {first}",
            first == second ? "" : $"after the primary was removed the temp folder changed: {first}, then {second}",
            Directory.Exists(second) ? "" : $"the removed primary was not created again: {second}"
        );
    }

    // A fallback is never recreated by name: once removed, a fresh one is resolved.
    private static string RemovedFallbackFailure(string unusablePrimary)
    {
        File.WriteAllText(unusablePrimary, "");
        AddonTempFolder.ResetForChecks(unusablePrimary);
        string first = AddonTempFolder.Get();
        Directory.Delete(first, recursive: true);
        string second = AddonTempFolder.Get();
        bool recreated = Directory.Exists(first);
        DeleteFallback(second, unusablePrimary);
        return Failures(
            SameFolder(first, unusablePrimary) ? $"a file as the staged primary was used as the folder: {first}" : "",
            first == second ? $"the removed fallback was used again: {second}" : "",
            recreated ? $"the removed fallback was created again by name: {first}" : ""
        );
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    // Removes a fallback folder a resolve made, never the primary it was given.
    private static void DeleteFallback(string folder, string? primary)
    {
        if ((primary is null || !SameFolder(folder, primary)) && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
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

        string tempFolder = AddonTempFolder.Get();
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

    // C22 saves an unedited scene, whose values still match its entry; this one edits the keyed material first, so only the
    // id its load minted pairs it with the entry.
    private static string EditingAKeyedResourceKeepsItsKey()
    {
        string path = SavesFolder + "edited-keys.xscn";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        File.WriteAllText(ProjectSettings.GlobalizePath(path), KeysScene);
        if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {path} did not return a PackedScene";
        }

        Node root = scene.Instantiate();
        string failure;
        try
        {
            if (root.GetNodeOrNull<Sprite2D>("One")?.Material is not CanvasItemMaterial material)
            {
                return "the scene's One has no CanvasItemMaterial";
            }

            material.LightMode = CanvasItemMaterial.LightModeEnum.LightOnly;
            failure = PackAndSave(root, path, "saving the edited scene");
        }
        finally
        {
            root.Free();
        }

        GC.KeepAlive(scene);
        if (failure.Length > 0)
        {
            return failure;
        }

        string saved = File.ReadAllText(ProjectSettings.GlobalizePath(path));
        int keys = Occurrences(saved, "x:Key=\"HandKey\"");
        int uses = Occurrences(saved, "Material=\"{HandKey}\"");
        return keys == 1 && uses == 2 ? "" : $"the saved text has x:Key=\"HandKey\" {keys} times and Material=\"{{HandKey}}\" {uses} times:\n{saved}";
    }

    // Make Local gives B's F1 and F2 A's box, which loaded with the same scene-unique id as B's own box; Godot's text saver
    // keeps the id on the first of the two it meets, A's. Only B's own box pairs with B's entry, so it keeps the key and
    // the comment, and A's box takes the next key.
    private static string ForeignSubResourceDoesNotTakeAKey() =>
        ForeignBoxFailure(
            "foreign-two",
            ["F1", "F2"],
            null,
            (text, _) =>
                Failures(
                    OwnBoxFailure(text, "0, 0, 1, 1"),
                    text.Contains("<StyleBoxFlat x:Key=\"StyleBoxFlat2\" BgColor=\"1, 0, 0, 1\" />", StringComparison.Ordinal)
                        ? ""
                        : "A's red box is not StyleBoxFlat2"
                )
        );

    // As C54 with B's own box edited before the save, so its value no longer matches its entry and only the id pairs it.
    private static string ForeignSubResourceDoesNotTakeAnEditedKey() =>
        ForeignBoxFailure("foreign-edited", ["F1", "F2"], new Color(0, 1, 0), (text, _) => OwnBoxFailure(text, "0, 1, 0, 1"));

    // A's box on F1 alone goes inline, and an inline resource with an entry's id drops the entry's key; A's box is not B's
    // own, so B's entry keeps its key and its comment, and nothing is dropped.
    private static string OneForeignUserKeepsTheOwnComment() =>
        ForeignBoxFailure(
            "foreign-one",
            ["F1"],
            null,
            (text, warnings) =>
                Failures(
                    OwnBoxFailure(text, "0, 0, 1, 1"),
                    warnings.FirstOrDefault(w => w.Contains(ConversionCodes.CommentDropped, StringComparison.Ordinal)) is { } dropped
                        ? $"the save warned: {dropped}"
                        : ""
                )
        );

    // ResourceSaver hands the saver the path unlocalized and the takeover names the sub-resources with it, as Godot's text
    // saver does, so after a save through res://<folder>/../<folder>/b.xscn the box's path holds the "..". B's own box,
    // edited so only its id pairs it, still keeps its key and comment on the next save through the same path.
    private static string NonCanonicalPathKeepsTheKey()
    {
        const string name = "non-canonical";
        string folder = OutFolder + name + "/";
        string path = folder + "b.xscn";
        string rawPath = folder + "../" + name + "/b.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(folder));
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), ForeignBScene);
            if (ResourceLoader.Load(path) is not PackedScene scene)
            {
                return $"loading {path} did not return a PackedScene";
            }

            Node root = scene.Instantiate();
            string failure;
            try
            {
                if (root.GetNodeOrNull("Own1")?.Get("theme_override_styles/panel").AsGodotObject() is not StyleBoxFlat own)
                {
                    return "B's Own1 has no StyleBoxFlat";
                }

                failure = PackAndSave(root, rawPath, "the first save through the non-canonical path");
                if (failure.Length == 0)
                {
                    own.BgColor = new Color(0, 1, 0);
                    failure = PackAndSave(root, rawPath, "the second save through the non-canonical path");
                }
            }
            finally
            {
                root.Free();
            }

            GC.KeepAlive(scene);
            return failure.Length > 0
                ? failure
                : OwnBoxFailure(File.ReadAllText(ProjectSettings.GlobalizePath(path)).ReplaceLineEndings("\n"), "0, 1, 0, 1");
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // As C54, then from the same session: B's save took A's live box over (its path now names b.xscn), and A's own box,
    // edited so only its id pairs it, is saved with A. A's entry keeps its key and its comment.
    private static string ForeignOwnerKeepsItsKeyAfterTheTakeover()
    {
        string folder = OutFolder + "foreign-owner/";
        string aPath = folder + "a.xscn";
        string bPath = folder + "b.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(folder));
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(aPath), ForeignACommentedScene);
            File.WriteAllText(ProjectSettings.GlobalizePath(bPath), ForeignBScene);
            if (ResourceLoader.Load(aPath) is not PackedScene a || ResourceLoader.Load(bPath) is not PackedScene b)
            {
                return $"loading {aPath} or {bPath} did not return a PackedScene";
            }

            Node aRoot = a.Instantiate();
            Node bRoot = b.Instantiate();
            string failure;
            string takenOver = "";
            try
            {
                failure = GiveForeignBox(aRoot, bRoot, ["F1", "F2"], null);
                failure = failure.Length > 0 ? failure : PackAndSave(bRoot, bPath, "saving B");
                if (failure.Length == 0)
                {
                    var box = (StyleBoxFlat)aRoot.GetNode("P1").Get("theme_override_styles/panel").AsGodotObject();
                    takenOver = box.ResourcePath;
                    box.BgColor = new Color(1, 1, 0);
                    failure = PackAndSave(aRoot, aPath, "saving A");
                }
            }
            finally
            {
                aRoot.Free();
                bRoot.Free();
            }

            GC.KeepAlive(a);
            GC.KeepAlive(b);
            return failure.Length > 0
                ? failure
                : ARedBoxFailure(File.ReadAllText(ProjectSettings.GlobalizePath(aPath)).ReplaceLineEndings("\n"), takenOver);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A's edited box keeps its key with its comment directly above it, and its two panels use it.
    private static string ARedBoxFailure(string text, string takenOver)
    {
        string entry = $"{ARedBoxComment}\n    <StyleBoxFlat x:Key=\"StyleBoxFlat1\" BgColor=\"1, 1, 0, 1\" />";
        int uses = Occurrences(text, OwnKeyUse);
        string verdict = Failures(
            text.Contains(entry, StringComparison.Ordinal) ? "" : "the text has no StyleBoxFlat1 of 1, 1, 0, 1 under A's comment",
            uses == 2 ? "" : $"{OwnKeyUse} appears {uses} times, not twice"
        );
        return verdict.Length == 0 ? "" : $"{verdict} (A's box was at \"{takenOver}\" before A's save):\n{text}";
    }

    private static string Failures(params string[] failures) => string.Join("; ", failures.Where(f => f.Length > 0));

    // B's own box keeps its key with its comment directly above it, and its two panels use it.
    private static string OwnBoxFailure(string text, string color)
    {
        string entry = $"{OwnBoxComment}\n    <StyleBoxFlat x:Key=\"StyleBoxFlat1\" BgColor=\"{color}\" />";
        int uses = Occurrences(text, OwnKeyUse);
        string failure = Failures(
            text.Contains(entry, StringComparison.Ordinal) ? "" : $"the text has no StyleBoxFlat1 of {color} under B's comment",
            uses == 2 ? "" : $"{OwnKeyUse} appears {uses} times, not twice"
        );
        return failure.Length == 0 ? "" : $"{failure}:\n{text}";
    }

    // Writes A and B, loads both into the cache, gives the named nodes of B A's box (and B's own box a new colour), saves B
    // and hands its text and the warnings the save logged to the verdict.
    private static string ForeignBoxFailure(string name, string[] foreignUsers, Color? ownColor, Func<string, IReadOnlyList<string>, string> verdict)
    {
        string folder = OutFolder + name + "/";
        string aPath = folder + "a.xscn";
        string bPath = folder + "b.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(folder));
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(aPath), ForeignAScene);
            File.WriteAllText(ProjectSettings.GlobalizePath(bPath), ForeignBScene);
            if (ResourceLoader.Load(aPath) is not PackedScene a || ResourceLoader.Load(bPath) is not PackedScene b)
            {
                return $"loading {aPath} or {bPath} did not return a PackedScene";
            }

            Node aRoot = a.Instantiate();
            Node bRoot = b.Instantiate();
            var log = new WarningLog();
            string failure;
            try
            {
                failure = GiveForeignBox(aRoot, bRoot, foreignUsers, ownColor);
                if (failure.Length == 0)
                {
                    OS.AddLogger(log);
                    try
                    {
                        failure = PackAndSave(bRoot, bPath, "saving B");
                    }
                    finally
                    {
                        OS.RemoveLogger(log);
                    }
                }
            }
            finally
            {
                aRoot.Free();
                bRoot.Free();
            }

            GC.KeepAlive(a);
            GC.KeepAlive(b);
            return failure.Length > 0
                ? failure
                : verdict(File.ReadAllText(ProjectSettings.GlobalizePath(bPath)).ReplaceLineEndings("\n"), log.Messages);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string GiveForeignBox(Node aRoot, Node bRoot, string[] foreignUsers, Color? ownColor)
    {
        if (aRoot.GetNodeOrNull("P1")?.Get("theme_override_styles/panel").AsGodotObject() is not StyleBoxFlat foreign)
        {
            return "A's P1 has no StyleBoxFlat";
        }

        if (bRoot.GetNodeOrNull("Own1")?.Get("theme_override_styles/panel").AsGodotObject() is not StyleBoxFlat own)
        {
            return "B's Own1 has no StyleBoxFlat";
        }

        if (foreign.ResourceSceneUniqueId != own.ResourceSceneUniqueId)
        {
            return $"setup: A's box loaded as {foreign.ResourceSceneUniqueId} and B's as {own.ResourceSceneUniqueId}; the check needs one id";
        }

        foreach (string user in foreignUsers)
        {
            bRoot.GetNode<Control>(user).AddThemeStyleboxOverride("panel", foreign);
        }

        if (ownColor is { } color)
        {
            own.BgColor = color;
        }

        return ForeignMetFirstFailure(bRoot, foreign, own);
    }

    // The checks hold only when Godot's text saver meets A's box first: it keeps a repeated id on the first of two resources
    // it meets and re-mints the other's on the live resource. A save to user://, where it takes over no path, shows which
    // it met first; both ids are put back after it, so B's save meets the same clash.
    private static string ForeignMetFirstFailure(Node bRoot, Resource foreign, Resource own)
    {
        string id = own.ResourceSceneUniqueId;
        string orderPath = SavesFolder + "foreign-order.tscn";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SavesFolder));
        var scene = new PackedScene();
        Error packed = scene.Pack(bRoot);
        Error saved = packed == Error.Ok ? ResourceSaver.Save(scene, orderPath) : packed;
        string foreignAfter = foreign.ResourceSceneUniqueId;
        string ownAfter = own.ResourceSceneUniqueId;
        foreign.ResourceSceneUniqueId = id;
        own.ResourceSceneUniqueId = id;
        GC.KeepAlive(scene);
        File.Delete(ProjectSettings.GlobalizePath(orderPath));
        if (saved != Error.Ok)
        {
            return $"setup: the save that finds the saver's order returned {saved}";
        }

        return foreignAfter == id && ownAfter != id
            ? ""
            : $"setup: Godot's text saver did not meet A's box first (A's box became {foreignAfter}, B's {ownAfter}, from {id})";
    }

    // The errors and warnings Godot logs while it is added, which may come from any thread, each with the severity Godot gave it.
    private sealed partial class WarningLog : Logger
    {
        private readonly List<(Logger.ErrorType Type, string Text)> entries = [];

        /// <summary>The log's entries in order, each with the severity it was logged as.</summary>
        public IReadOnlyList<(Logger.ErrorType Type, string Text)> Entries
        {
            get
            {
                lock (entries)
                {
                    return [.. entries];
                }
            }
        }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (entries)
                {
                    return [.. entries.Select(entry => entry.Text)];
                }
            }
        }

        public override void _LogError(
            string function,
            string file,
            int line,
            string code,
            string rationale,
            bool editorNotify,
            int errorType,
            Godot.Collections.Array<ScriptBacktrace> scriptBacktraces
        )
        {
            lock (entries)
            {
                entries.Add(((Logger.ErrorType)errorType, $"{code} {rationale}"));
            }
        }
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
        File.WriteAllText(ProjectSettings.GlobalizePath(path), CleanScene);
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

    // Godot calls _SetUid for a file that exists; a path with no file gets no sidecar.
    private static string SetUidNeedsAFile()
    {
        const string path = OutFolder + "missing.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            string sidecar = ProjectSettings.GlobalizePath(path) + ".uid";
            using var saver = new XscnFormatSaver();
            Error error = saver._SetUid(path, ResourceUid.CreateId());
            string[] failures =
            [
                error == Error.FileNotFound ? "" : $"_SetUid({path}) returned {error}, not FileNotFound",
                File.Exists(sidecar) ? $"_SetUid({path}) wrote {sidecar}" : "",
            ];
            return string.Join("; ", failures.Where(f => f.Length > 0));
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // The text loader resolves a relative ext_resource path against the file it reads (resource_format_text.cpp:509-512),
    // so the converted text must name the .xscn's neighbours, not the temp file's.
    private static string RelativeInstanceLoads()
    {
        if (ResourceLoader.Load(RelativeHolderPath, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {RelativeHolderPath} did not return a PackedScene";
        }

        Node root = scene.Instantiate();
        try
        {
            const string expected = RelativeCasesPath + "sib.xscn";
            return root.GetNodeOrNull("Inst") switch
            {
                null => "the scene has no node Inst",
                { SceneFilePath: var file } when file != expected => $"Inst's SceneFilePath is \"{file}\", not \"{expected}\"",
                _ => root.GetNodeOrNull<Node2D>("Inst/Inner") is null ? "the scene has no Node2D Inst/Inner" : "",
            };
        }
        finally
        {
            root.Free();
        }
    }

    private static string RelativeResourceLoads()
    {
        if (ResourceLoader.Load(RelativeHolderPath, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
        {
            return $"loading {RelativeHolderPath} did not return a PackedScene";
        }

        Node root = scene.Instantiate();
        try
        {
            const string expected = RelativeCasesPath + "box.xres";
            return root.GetMeta("box", default(Variant)).Obj switch
            {
                StyleBoxFlat { ResourcePath: expected } => "",
                Resource other => $"metadata/box is a {other.GetClass()} at \"{other.ResourcePath}\", not the StyleBoxFlat at {expected}",
                _ => "metadata/box holds no resource",
            };
        }
        finally
        {
            root.Free();
        }
    }

    // Outside the editor the text saver writes no uid on an ext_resource (resource_format_text.cpp:1884 asks a callback only
    // the editor registers), and the x:Uids table is built from those uids.
    private static string HeadlessSaveKeepsUids()
    {
        const string path = OutFolder + "uids-save.xscn";
        DeleteFolder(OutFolder);
        try
        {
            WriteHolderWithThemeUid(path, withValueReference: false);
            if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
            {
                return $"loading {path} did not return a PackedScene";
            }

            Error saved = ResourceSaver.Save(scene, path);
            if (saved != Error.Ok)
            {
                return $"the save returned {saved}";
            }

            XscnParseResult parsed = ParseFile(path);
            string expected = UidTextOf(ThemePath);
            string? found = parsed.Document.Uids.FirstOrDefault(u => u.Path == ThemePath)?.Uid;
            return found == expected
                ? ""
                : $"after the save, {path}'s x:Uids gives {ThemePath} \"{found ?? "(no entry)"}\", not \"{expected}\":\n"
                    + File.ReadAllText(ProjectSettings.GlobalizePath(path));
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A file copied in after the last import has a .uid but no uid_cache.bin row, and outside the editor
    // ResourceLoader.get_resource_uid reads only that cache (resource_loader.cpp:1414-1415). The scene's own x:Uids keeps
    // the uid, under an absolute spelling and a relative one.
    private static string HeadlessSaveKeepsAnUncachedUid()
    {
        const string path = OutFolder + "uncached.xscn";
        const string absolute = OutFolder + "copied.xres";
        const string relative = OutFolder + "neighbour.xres";
        DeleteFolder(OutFolder);
        try
        {
            string absoluteUid = CopyWithNewUid(BoxPath, absolute);
            string relativeUid = CopyWithNewUid(BoxPath, relative);
            File.WriteAllText(
                ProjectSettings.GlobalizePath(path),
                $"<Panel xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\" ThemeOverrideStyles.panel=\"{absolute}\">\n"
                    + "  <x:Uids>\n"
                    + $"    <x:Uid Path=\"{absolute}\" Value=\"{absoluteUid}\" />\n"
                    + $"    <x:Uid Path=\"neighbour.xres\" Value=\"{relativeUid}\" />\n"
                    + "  </x:Uids>\n"
                    + "  <x:Prop Name=\"metadata/ref\" Value='Resource(\"neighbour.xres\")' />\n"
                    + "</Panel>\n"
            );
            if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
            {
                return $"loading {path} did not return a PackedScene";
            }

            Error saved = ResourceSaver.Save(scene, path);
            if (saved != Error.Ok)
            {
                return $"the save returned {saved}";
            }

            XscnParseResult parsed = ParseFile(path);
            string table = string.Join(", ", parsed.Document.Uids.OrderBy(u => u.Path, StringComparer.Ordinal).Select(u => $"{u.Path}={u.Uid}"));
            string expected = $"{absolute}={absoluteUid}, {relative}={relativeUid}";
            return table == expected ? "" : $"after the save, {path}'s x:Uids is [{table}], not [{expected}]";
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string CopyWithNewUid(string source, string path)
    {
        string target = ProjectSettings.GlobalizePath(path);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        File.Copy(ProjectSettings.GlobalizePath(source), target);
        string uid = ResourceUid.IdToText(ResourceUid.CreateId());
        File.WriteAllText(target + ".uid", uid + "\n");
        return uid;
    }

    // A .remap sends the load to a file in another folder; the text loader still resolves relative paths against the path
    // asked for (resource_format_text.cpp:1445, resource_loader.cpp:541).
    private static string RemappedFileResolvesAgainstItsOriginalPath()
    {
        const string asked = OutFolder + "asked.xscn";
        const string real = OutFolder + "elsewhere/real.xscn";
        const string sibling = OutFolder + "sib.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder + "elsewhere/"));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(RelativeCasesPath + "sib.xscn"), ProjectSettings.GlobalizePath(sibling));
            File.WriteAllText(ProjectSettings.GlobalizePath(asked + ".remap"), $"[remap]\n\npath=\"{real}\"\n");
            File.WriteAllText(
                ProjectSettings.GlobalizePath(real),
                "<Node xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
                    + "  <x:Instance Scene=\"sib.xscn\" Name=\"Inst\" />\n"
                    + "</Node>\n"
            );
            if (ResourceLoader.Load(asked, "", ResourceLoader.CacheMode.Ignore) is not PackedScene scene)
            {
                return $"loading {asked} did not return a PackedScene";
            }

            Node root = scene.Instantiate();
            try
            {
                string? file = root.GetNodeOrNull("Inst")?.SceneFilePath;
                return file == sibling ? "" : $"Inst's SceneFilePath is \"{file ?? "(no node Inst)"}\", not \"{sibling}\"";
            }
            finally
            {
                root.Free();
            }
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
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

    private static string SaveTakesOverSubResourcePaths()
    {
        const string path = OutFolder + "taken.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            // Godot's property order puts normal before hover, the alphabetical order hover first; hover has an id already.
            var normal = new StyleBoxFlat();
            var hover = new StyleBoxFlat { ResourceSceneUniqueId = "StyleBoxFlat_given" };
            var root = new Control { Name = "Root" };
            var button = new Button { Name = "Button" };
            button.Set("theme_override_styles/normal", normal);
            button.Set("theme_override_styles/hover", hover);
            root.AddChild(button);
            button.Owner = root;
            var scene = new PackedScene();
            Error packed = scene.Pack(root);
            root.Free();
            if (packed != Error.Ok)
            {
                return $"packing the scene returned {packed}";
            }

            Error saved = ResourceSaver.Save(scene, path, ResourceSaver.SaverFlags.ReplaceSubresourcePaths);
            if (saved != Error.Ok)
            {
                return $"the save returned {saved}";
            }

            string failures = SavedBoxFailures(path, [normal, hover]);
            GC.KeepAlive(scene);
            return failures;
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // Save As onto a file that exists: the file's previous text is carried, and the boxes still move from the first file
    // to the one written last.
    private static string SaveOverAnExistingFileTakesOverTheBox()
    {
        const string first = OutFolder + "first.xscn";
        const string path = OutFolder + "existing.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            var normal = new StyleBoxFlat();
            var hover = new StyleBoxFlat { ResourceSceneUniqueId = "StyleBoxFlat_given" };
            var root = new Control { Name = "Root" };
            var button = new Button { Name = "Button" };
            button.Set("theme_override_styles/normal", normal);
            button.Set("theme_override_styles/hover", hover);
            root.AddChild(button);
            button.Owner = root;
            var scene = new PackedScene();
            Error packed = scene.Pack(root);
            root.Free();
            if (packed != Error.Ok)
            {
                return $"packing the scene returned {packed}";
            }

            Error saved = ResourceSaver.Save(scene, first, ResourceSaver.SaverFlags.ReplaceSubresourcePaths);
            if (saved != Error.Ok)
            {
                return $"the first save returned {saved}";
            }

            File.Copy(ProjectSettings.GlobalizePath(first), ProjectSettings.GlobalizePath(path));
            saved = ResourceSaver.Save(scene, path, ResourceSaver.SaverFlags.ReplaceSubresourcePaths);
            if (saved != Error.Ok)
            {
                return $"the save over {path} returned {saved}";
            }

            string failures = SavedBoxFailures(path, [normal, hover]);
            GC.KeepAlive(scene);
            return failures;
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A faithful Save As from a loaded file whose nodes carry no ids yet, onto a file that exists with the same text: the
    // boxes loaded from the first file, so they are not the target's own, and they move to the file written last, as in C44.
    private static string SaveAsFromALoadedFileTakesOverTheBoxes()
    {
        const string first = OutFolder + "first.xscn";
        const string path = OutFolder + "existing.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(first), SaveAsFirstScene);
            File.Copy(ProjectSettings.GlobalizePath(first), ProjectSettings.GlobalizePath(path));
            if (ResourceLoader.Load(first) is not PackedScene scene)
            {
                return $"loading {first} did not return a PackedScene";
            }

            Node root = scene.Instantiate();
            Resource[] boxes;
            string failure;
            try
            {
                Node button = root.GetNode("Button");
                if (
                    button.Get("theme_override_styles/normal").AsGodotObject() is not Resource normal
                    || button.Get("theme_override_styles/hover").AsGodotObject() is not Resource hover
                )
                {
                    return $"{first}'s Button has no normal and hover boxes";
                }

                boxes = [normal, hover];
                failure = PackAndSave(root, path, "the Save As");
            }
            finally
            {
                root.Free();
            }

            GC.KeepAlive(scene);
            return failure.Length > 0 ? failure : SavedBoxFailures(path, boxes);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A rename moves the box's site, so the next load mints it another id: the save gives the live box that id, and a
    // replace load then reuses it. Packing gives each node a unique id (packed_scene.cpp:1099-1119), so a first save of the
    // tree writes them and the rename's save matches the renamed node by its id.
    private static string RenameKeepsTheLiveSubResource()
    {
        const string path = OutFolder + "renamed.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(HolderPath), ProjectSettings.GlobalizePath(path));
            if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Replace) is not PackedScene scene)
            {
                return $"the replace load of {path} did not return a PackedScene";
            }

            Node root = scene.Instantiate();
            Resource? box;
            string failure;
            try
            {
                Node inline = root.GetNode("Inline");
                box = inline.Get("theme_override_styles/panel").AsGodotObject() as Resource;
                failure = PackAndSave(root, path, "the first save");
                inline.Name = "Renamed";
                failure = failure.Length > 0 ? failure : PackAndSave(root, path, "the save of the renamed tree");
            }
            finally
            {
                root.Free();
            }

            GC.KeepAlive(scene);
            if (failure.Length > 0)
            {
                return failure;
            }

            return box is null ? $"{path} gave no inline StyleBox" : RenamedBoxFailure(path, box);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string PackAndSave(Node root, string path, string what)
    {
        var scene = new PackedScene();
        Error packed = scene.Pack(root);
        if (packed != Error.Ok)
        {
            return $"packing the tree for {what} returned {packed}";
        }

        Error saved = ResourceSaver.Save(scene, path, ResourceSaver.SaverFlags.ReplaceSubresourcePaths);
        GC.KeepAlive(scene);
        return saved == Error.Ok ? "" : $"{what} returned {saved}";
    }

    private static string RenamedBoxFailure(string path, Resource box)
    {
        string savedPath = box.ResourcePath;
        if (savedPath != path + "::" + box.ResourceSceneUniqueId)
        {
            return $"the box's path is \"{savedPath}\", not \"{path}::{box.ResourceSceneUniqueId}\"";
        }

        if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Replace) is not PackedScene reloaded)
        {
            return $"the replace load of {path} after the rename did not return a PackedScene";
        }

        Node again = reloaded.Instantiate();
        Variant after = again.GetNodeOrNull("Renamed")?.Get("theme_override_styles/panel") ?? default;
        again.Free();
        GC.KeepAlive(reloaded);
        return SameBoxFailure(0, box, savedPath, after);
    }

    // Each saved box is named after its id, and a replace load of the file gives back the same object at the same path.
    private static string SavedBoxFailures(string path, Resource[] boxes)
    {
        string[] savedPaths = [.. boxes.Select(box => box.ResourcePath)];
        if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Replace) is not PackedScene reloaded)
        {
            return $"the replace load of {path} did not return a PackedScene";
        }

        Node again = reloaded.Instantiate();
        Node button = again.GetNode("Button");
        Variant[] after = [button.Get("theme_override_styles/normal"), button.Get("theme_override_styles/hover")];
        again.Free();
        GC.KeepAlive(reloaded);
        string[] failures =
        [
            .. boxes.Select(
                (box, i) =>
                    box.ResourcePath == path + "::" + box.ResourceSceneUniqueId
                        ? ""
                        : $"box {i}'s path is \"{box.ResourcePath}\", not \"{path}::{box.ResourceSceneUniqueId}\""
            ),
            .. boxes.Select((box, i) => SameBoxFailure(i, box, savedPaths[i], after[i])),
        ];
        return string.Join("; ", failures.Where(f => f.Length > 0));
    }

    private static string SameBoxFailure(int index, Resource box, string savedPath, Variant reloaded)
    {
        var live = reloaded.AsGodotObject() as Resource;
        return live is not null && live.GetInstanceId() == box.GetInstanceId() && live.ResourcePath == savedPath
            ? ""
            : $"box {index}'s reload is {live?.GetInstanceId()} at \"{live?.ResourcePath}\", not {box.GetInstanceId()} at \"{savedPath}\"";
    }

    private static string SaveDoesNotTakeOverWithoutTheFlag()
    {
        const string path = OutFolder + "untaken.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            var box = new StyleBoxFlat();
            var root = new Control { Name = "Root" };
            var panel = new Panel { Name = "Panel" };
            panel.Set("theme_override_styles/panel", box);
            root.AddChild(panel);
            panel.Owner = root;
            var scene = new PackedScene();
            Error packed = scene.Pack(root);
            root.Free();
            if (packed != Error.Ok)
            {
                return $"packing the scene returned {packed}";
            }

            Error saved = ResourceSaver.Save(scene, path);
            if (saved != Error.Ok)
            {
                return $"the save returned {saved}";
            }

            return box.ResourcePath.Length == 0 ? "" : $"the sub-resource's path is \"{box.ResourcePath}\", not empty";
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string LoadRegistersSubResources()
    {
        Resource? box = LiveInlineBox(HolderPath, ResourceLoader.CacheMode.Replace, out PackedScene? scene);
        if (box is null)
        {
            return $"{HolderPath} gave no inline StyleBox";
        }

        bool cached = ResourceLoader.HasCached(box.ResourcePath);
        GC.KeepAlive(scene);
        if (!box.ResourcePath.StartsWith(HolderPath + "::", StringComparison.Ordinal))
        {
            return $"the box's path \"{box.ResourcePath}\" does not name {HolderPath}";
        }

        return cached ? "" : $"HasCached(\"{box.ResourcePath}\") is false";
    }

    private Task<string> ReuseKeepsADroppedScenesSubResource() => ReuseKeepsTheObject(HolderPath);

    private async Task<string> ReuseKeepsADroppedScenesSubResourceInATscn()
    {
        const string path = OutFolder + "reused.tscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        File.WriteAllText(
            ProjectSettings.GlobalizePath(path),
            "[gd_scene format=3]\n\n[sub_resource type=\"StyleBoxFlat\" id=\"box\"]\n\n[node name=\"Root\" type=\"Control\"]\n\n"
                + "[node name=\"Inline\" type=\"Panel\" parent=\".\"]\ntheme_override_styles/panel = SubResource(\"box\")\n"
        );
        try
        {
            return await ReuseKeepsTheObject(path);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A .tscn whose last reference is let go leaves the cache at once. Godot's dispatch of _Load keeps a copy of the Variant
    // it returns until a finalizer runs, so the loader lets go of it a frame later.
    private async Task<string> DroppedSceneLeavesTheCache()
    {
        const string path = OutFolder + "dropped.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(HolderPath), ProjectSettings.GlobalizePath(path));
            Resource? loaded = ResourceLoader.Load(path);
            if (loaded is null)
            {
                return $"{path} did not load";
            }

            loaded.Dispose();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return ResourceLoader.HasCached(path) ? $"{path} is still cached a frame after its only reference was disposed" : "";
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A scene an engine caller loads (the editor, GDScript) leaves the cache once that caller lets go, as a .tscn does:
    // the loader keeps no C# wrapper of the resource it returns. The load goes through the engine's ResourceLoader object,
    // so the check never makes a wrapper of the scene itself; no garbage collection runs.
    private async Task<string> EngineDroppedSceneLeavesTheCache()
    {
        const string path = OutFolder + "engine.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(HolderPath), ProjectSettings.GlobalizePath(path));
            using (Variant loaded = Engine.GetSingleton("ResourceLoader").Call("load", path))
            {
                if (loaded.VariantType != Variant.Type.Object)
                {
                    return $"{path} did not load";
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return ResourceLoader.HasCached(path) ? $"{path} is still cached a frame after the engine caller let go" : "";
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A sub-resource held only inside an array leaves the cache once its scene is dropped, so the walk that names it keeps
    // no strong reference: it lets go of every container it reads, and the C# wrappers it makes release at GC, each pass
    // freeing the next link of the chain. Its path comes from the loader's own conversion, so the check never holds the
    // resource itself.
    private async Task<string> DroppedSceneReleasesContainerSubResources()
    {
        const string path = OutFolder + "contained.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(ContainedPath), ProjectSettings.GlobalizePath(path));
            XscnParseResult parsed = XscnParser.Parse(
                File.ReadAllText(ProjectSettings.GlobalizePath(path)),
                XscnDocumentKind.Scene,
                XscnMetadata.Value
            );
            string subPath = path + "::" + XscnLoading.ToTscn(parsed.Document).Document.SubResources.Single().Id;
            Resource? loaded = ResourceLoader.Load(path);
            if (loaded is null)
            {
                return $"{path} did not load";
            }

            bool cachedWhileHeld = ResourceLoader.HasCached(subPath);
            loaded.Dispose();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!cachedWhileHeld)
            {
                return $"{subPath} was not cached while its scene was held";
            }

            for (int pass = 0; pass < ContainerReleaseGcPasses && ResourceLoader.HasCached(subPath); pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            return ResourceLoader.HasCached(subPath)
                ? $"{subPath} is still cached after its scene was dropped and {ContainerReleaseGcPasses} garbage collections"
                : "";
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A resource's array property holds its items the way a plain property holds its value, and the walk disposes every
    // container it reads: a dropped .xres releases one box as soon as the other, before any collection. Both wrappers are
    // taken the same way, so equal counts is what the walk leaves.
    private async Task<string> DroppedResourceReleasesContainerSubResources()
    {
        const string path = OutFolder + "container.xres";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(ContainerPath), ProjectSettings.GlobalizePath(path));
            XscnParseResult parsed = XscnParser.Parse(
                File.ReadAllText(ProjectSettings.GlobalizePath(path)),
                XscnDocumentKind.Resource,
                XscnMetadata.Value
            );
            string[] subPaths = [.. XscnLoading.ToTscn(parsed.Document).Document.SubResources.Select(sub => path + "::" + sub.Id)];
            if (subPaths.Length != 2)
            {
                return $"{path} converts to {subPaths.Length} sub-resources, not 2";
            }

            Resource? loaded = ResourceLoader.Load(path);
            if (loaded is null)
            {
                return $"{path} did not load";
            }

            Resource? first = ResourceLoader.GetCachedRef(subPaths[0]);
            Resource? second = ResourceLoader.GetCachedRef(subPaths[1]);
            loaded.Dispose();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (first is null || second is null)
            {
                return $"{string.Join(" and ", subPaths)} not cached while {path} was held";
            }

            (int listed, int plain) = ContainerBoxCounts(first, second);
            string counts =
                $"{path}: the box in metadata/list holds {listed} references and the box in Button/styles/normal holds"
                + $" {plain}, a frame after the resource was dropped";
            return listed == plain ? "" : counts;
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // The reference counts of the box only the array holds and the box a plain property holds, in that order; the fixture
    // gives the array's box the colour the other box does not have.
    private static (int Listed, int Plain) ContainerBoxCounts(Resource first, Resource second) =>
        first.Get("bg_color").AsColor() == ListedBoxColor
            ? (first.GetReferenceCount(), second.GetReferenceCount())
            : (second.GetReferenceCount(), first.GetReferenceCount());

    // The box stays alive while its scene leaves the cache; a reuse load of the scene then reuses the box (:589-592).
    private async Task<string> ReuseKeepsTheObject(string path)
    {
        Resource? box = LiveInlineBox(path, ResourceLoader.CacheMode.Replace, out PackedScene? scene);
        if (box is null)
        {
            return $"{path} gave no inline StyleBox";
        }

        // Disposing the wrapper is how C# lets go of a RefCounted; the instantiated root is already freed.
        string expected = box.ResourcePath;
        scene?.Dispose();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (ResourceLoader.HasCached(path))
        {
            return $"{path} is still cached a frame after its scene's wrapper was disposed";
        }

        Resource? again = LiveInlineBox(path, ResourceLoader.CacheMode.Reuse, out PackedScene? againScene);
        GC.KeepAlive(againScene);
        if (again is null)
        {
            return $"the reuse load of {path} gave no inline StyleBox";
        }

        return again.GetInstanceId() == box.GetInstanceId() && again.ResourcePath == expected
            ? ""
            : $"the reuse load gave {again.GetInstanceId()} at \"{again.ResourcePath}\", not {box.GetInstanceId()} at \"{expected}\"";
    }

    // A script variable that is not exported is not stored, so the saver does not save what it holds: here another
    // scene's sub-resource, whose path the save must leave alone.
    private static string SaveLeavesANonStoredResourceAlone()
    {
        const string path = OutFolder + "held.xscn";
        Resource? foreign = LiveInlineBox(HolderPath, ResourceLoader.CacheMode.Replace, out PackedScene? holderScene);
        if (foreign is null)
        {
            return $"{HolderPath} gave no inline StyleBox";
        }

        string expected = foreign.ResourcePath;
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            var holding = new Resource();
            holding.SetScript(GD.Load<Script>(HeldScriptPath));
            holding.Set("held", foreign);
            var root = new Control { Name = "Root" };
            root.SetMeta("holding", holding);
            // The file's own box takes the foreign box's id, so the id is in the save's map and only the walk's storage
            // rule keeps the foreign box out of it.
            root.SetMeta("own", new StyleBoxFlat { ResourceSceneUniqueId = foreign.ResourceSceneUniqueId });
            var scene = new PackedScene();
            Error packed = scene.Pack(root);
            root.Free();
            if (packed != Error.Ok)
            {
                return $"packing the scene returned {packed}";
            }

            Error saved = ResourceSaver.Save(scene, path, ResourceSaver.SaverFlags.ReplaceSubresourcePaths);
            GC.KeepAlive(holderScene);
            GC.KeepAlive(scene);
            if (saved != Error.Ok)
            {
                return $"the save returned {saved}";
            }

            string[] failures =
            [
                holding.Get("held").AsGodotObject() == foreign ? "" : "the script variable no longer holds the box",
                holding.ResourcePath.StartsWith(path + "::", StringComparison.Ordinal)
                    ? ""
                    : $"the holding resource's path is \"{holding.ResourcePath}\"",
                foreign.ResourcePath == expected ? "" : $"the held box's path is \"{foreign.ResourcePath}\", not \"{expected}\"",
            ];
            return string.Join("; ", failures.Where(f => f.Length > 0));
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string ReplaceKeepsALiveSubResource() => ReplaceKeepsTheObject(HolderPath);

    private static string ReplaceKeepsALiveSubResourceInATscn()
    {
        const string path = OutFolder + "live.tscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        File.WriteAllText(
            ProjectSettings.GlobalizePath(path),
            "[gd_scene format=3]\n\n[sub_resource type=\"StyleBoxFlat\" id=\"box\"]\n\n[node name=\"Root\" type=\"Control\"]\n\n"
                + "[node name=\"Inline\" type=\"Panel\" parent=\".\"]\ntheme_override_styles/panel = SubResource(\"box\")\n"
        );
        try
        {
            return ReplaceKeepsTheObject(path);
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    private static string ReplaceKeepsTheObject(string path)
    {
        Resource? first = LiveInlineBox(path, ResourceLoader.CacheMode.Replace, out PackedScene? firstScene);
        Resource? second = LiveInlineBox(path, ResourceLoader.CacheMode.Replace, out PackedScene? secondScene);
        GC.KeepAlive(firstScene);
        GC.KeepAlive(secondScene);
        if (first is null || second is null)
        {
            return $"{path} gave no inline StyleBox";
        }

        return first.GetInstanceId() == second.GetInstanceId()
            ? ""
            : $"the second load made a new object: {first.GetInstanceId()} then {second.GetInstanceId()}, path \"{first.ResourcePath}\"";
    }

    private static Resource? LiveInlineBox(string path, ResourceLoader.CacheMode mode, out PackedScene? scene)
    {
        scene = ResourceLoader.Load(path, "", mode) as PackedScene;
        if (scene is null)
        {
            return null;
        }

        Node root = scene.Instantiate();
        try
        {
            return root.GetNodeOrNull<Panel>("Inline")?.Get("theme_override_styles/panel").AsGodotObject() as Resource;
        }
        finally
        {
            root.Free();
        }
    }

    // The broken copy holds a sub-resource of an abstract class: the conversion accepts it and Godot's text loader cannot
    // create it (resource_format_text.cpp:596-608). A missing external file would not do, since a scene skips it and loads.
    // The temp file a failed load keeps is the proof that the failure came from the text loader.
    private static string ReplaceRestoresAfterAFailedLoad()
    {
        const string copyPath = OutFolder + "restore.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        string holder = File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath));
        int tempPathsBeforeFailure = XscnLoadRoute.TempPaths.Count;
        Resource? held = LiveInlineBox(HolderPath, ResourceLoader.CacheMode.Replace, out PackedScene? heldScene);
        string heldPath = held?.ResourcePath ?? "";
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(copyPath), holder);
            Resource? box = LiveInlineBox(copyPath, ResourceLoader.CacheMode.Replace, out PackedScene? scene);
            if (held is null || box is null)
            {
                return $"{HolderPath} or {copyPath} gave no inline StyleBox";
            }

            string expected = box.ResourcePath;
            File.WriteAllText(
                ProjectSettings.GlobalizePath(copyPath),
                holder.Replace("</Control>", AbstractSubResource + "</Control>", StringComparison.Ordinal)
            );
            tempPathsBeforeFailure = XscnLoadRoute.TempPaths.Count;
            ResourceLoader.Load(copyPath, "", ResourceLoader.CacheMode.Replace);
            bool failedInGodot = XscnLoadRoute.TempPaths.Skip(tempPathsBeforeFailure).Any(File.Exists);
            GC.KeepAlive(heldScene);
            GC.KeepAlive(scene);
            string[] failures =
            [
                failedInGodot ? "" : "the broken copy did not fail in Godot's text loader",
                StayedFailure(copyPath, expected, box),
                StayedFailure(HolderPath, heldPath, held),
            ];
            return string.Join("; ", failures.Where(f => f.Length > 0));
        }
        finally
        {
            foreach (string kept in XscnLoadRoute.TempPaths.Skip(tempPathsBeforeFailure).Where(File.Exists))
            {
                File.Delete(kept);
            }

            DeleteFolder(OutFolder);
        }
    }

    // A box named after its file before the failed load and still at that path after it.
    private static string StayedFailure(string file, string before, Resource box) =>
        before.StartsWith(file + "::", StringComparison.Ordinal) && box.ResourcePath == before
            ? ""
            : $"{file}'s box moved from \"{before}\" to \"{box.ResourcePath}\"";

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

    // The root carries a C# script, so the load's wrapper may have been handed out while the file loaded: the load leaves
    // it alone, and the Count reads through it before and after a collection.
    private static string ScriptedResourceKeepsItsWrapper()
    {
        CheckedResource? loaded = ResourceLoader.Load<CheckedResource>(ScriptedCsPath);
        if (loaded is null)
        {
            return $"loading {ScriptedCsPath} gave null";
        }

        var failures = new List<string>();
        try
        {
            if (loaded.Count != 7)
            {
                failures.Add($"its Count is {loaded.Count}, not 7");
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (loaded.Count != 7)
            {
                failures.Add($"after a collection its Count is {loaded.Count}, not 7");
            }
        }
        catch (ObjectDisposedException)
        {
            return "the load disposed the resource's C# wrapper, so its Count cannot be read";
        }

        GC.KeepAlive(loaded);
        return string.Join("; ", failures);
    }

    // The script's setter hands its own resource to C#, which keeps it: the load therefore leaves that wrapper alone, and
    // what C# holds is the load's object and still answers after a collection.
    private static string ScriptHandsItselfToCSharp()
    {
        CheckHandoff.Held = null;
        Resource? loaded = ResourceLoader.Load(ScriptedGdPath);
        if (loaded is null)
        {
            return $"loading {ScriptedGdPath} gave null";
        }

        Resource? held = CheckHandoff.Held;
        if (held is null)
        {
            return $"loading {ScriptedGdPath} did not hand its resource to CheckHandoff";
        }

        string failure;
        try
        {
            failure = Failures(
                held.GetInstanceId() == loaded.GetInstanceId()
                    ? ""
                    : $"Held is object {held.GetInstanceId()}, not the load's {loaded.GetInstanceId()}",
                HeldCountFailure(held, "after the load")
            );
            GC.Collect();
            GC.WaitForPendingFinalizers();
            failure = Failures(failure, HeldCountFailure(held, "after a collection"));
        }
        catch (ObjectDisposedException)
        {
            return "the load disposed the wrapper the script handed to C#";
        }

        GC.KeepAlive(loaded);
        return failure;
    }

    // The count as the wrapper C# holds reads it back, or why it cannot.
    private static string HeldCountFailure(Resource held, string when)
    {
        int count = held.Get("count").AsInt32();
        return count == 5 ? "" : $"its count is {count} {when}, not 5";
    }
}
