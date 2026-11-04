using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GodotXaml.Addon;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
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

    // The managed budget of one no-GC region around a measured action; an action that spends it fails C72 by name.
    private const int NoGcRegionBytes = 32 * 1024 * 1024;

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

    // C74-C79's document path: no file has it and no other check loads it, so nothing is cached under it beforehand.
    private const string PreviewPath = CasesPath + "preview.xscn";

    private const string PreviewScriptsSetting = "godot_xaml/preview_server/allow_scripts";

    // C76 and C77's text: the relative holder with a node more than its file has, so the text is not the file's.
    private const string PreviewRelativeScene =
        "<Node2D xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Holder\">\n"
        + "  <x:Prop Name=\"metadata/box\" Value='Resource(\"box.xres\")' />\n"
        + "  <Node2D Name=\"Added\" />\n"
        + "</Node2D>\n";

    // C78's text: an inline box, so the converted scene has a sub-resource.
    private const string PreviewInlineScene =
        "<Control Name=\"Root\">\n"
        + "  <Panel Name=\"Panel\">\n"
        + "    <Panel.ThemeOverrideStyles.panel>\n"
        + "      <StyleBoxFlat BgColor=\"1, 0, 0, 1\" />\n"
        + "    </Panel.ThemeOverrideStyles.panel>\n"
        + "  </Panel>\n"
        + "</Control>\n";

    // C79's text: a scripted root with a Control property and a script property.
    private const string PreviewRootScriptPath = CasesPath + "preview_root.gd";

    private const string PreviewRanMeta = "gx_preview_ran";

    // C80's text: a built-in @tool script on the root whose static initializer marks the engine.
    private const string PreviewBuiltInScriptScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <Control.script>\n"
        + "    <GDScript Script.source='\"@tool&#10;extends Control&#10;&#10;static func _static_init():&#10;&#9;Engine.set_meta(\\\""
        + PreviewRanMeta
        + "\\\", true)&#10;\"' />\n"
        + "  </Control.script>\n"
        + "</Control>\n";

    // C81's text: a scripted emitter connected by its script's signal to an unscripted node, and an unscripted Button
    // connected to the scripted root's method.
    private const string PreviewSignalsScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\" Script=\""
        + CasesPath
        + "preview_signals.gd\">\n"
        + "  <Control Name=\"Emitter\" Script=\""
        + CasesPath
        + "preview_signals.gd\">\n"
        + "    <x:Connect Signal=\"poked\" To=\"Plain\" Method=\"hide\" />\n"
        + "  </Control>\n"
        + "  <Control Name=\"Plain\" />\n"
        + "  <Button Name=\"Ok\" Pressed=\"on_poked\">\n"
        + "    <x:Connect Signal=\"Pressed\" To=\"./Emitter\" Method=\"on_poked\" />\n"
        + "    <x:Connect Signal=\"ButtonDown\" To=\"Plain/../Emitter\" Method=\"on_poked\" />\n"
        + "  </Button>\n"
        + "</Control>\n";

    // C82's text: an instance of a scene with a scripted root, its script detached, connected to a plain node.
    private const string PreviewNullScriptScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Instance Scene=\""
        + CasesPath
        + "preview_scripted.xscn\" Name=\"Inst\" Script=\"{x:Null}\">\n"
        + "    <x:Connect GodotSignal=\"resized\" To=\"Plain\" Method=\"hide\" />\n"
        + "  </x:Instance>\n"
        + "  <Control Name=\"Plain\" />\n"
        + "</Control>\n";

    private const string PreviewScriptedScene =
        "<Control Name=\"Root\" Script=\"" + CasesPath + "preview_root.gd\" TooltipText=\"kept\" count=\"3\" />\n";

    // C83's instanced .xscn: a built-in @tool script on its root whose static initializer sets this meta on the engine.
    private const string PreviewInstancedRanMeta = "gx_preview_inst";

    // C84's instanced .tscn: a @tool script file on its root whose static initializer sets this meta on the engine.
    private const string PreviewInstancedFileRanMeta = "gx_preview_inst_file";

    private const string PreviewInstancedScriptPath = CasesPath + "preview_inst_script.gd";

    // C85's second level: B, instanced by A, has a built-in @tool script whose static initializer sets this meta.
    private const string PreviewDeepRanMeta = "gx_preview_deep";

    // C86's text: a typed array whose element type is a script, which the strip removes.
    private const string PreviewTypedScriptPath = CasesPath + "preview_typed.gd";

    private const string PreviewTypedArrayScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Prop Name=\"metadata/list\" Value='Array[Resource(\""
        + PreviewTypedScriptPath
        + "\")]([])' />\n"
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

    // A mounted pack cannot be unmounted, and Godot re-reads its files when the class list is refreshed: a folder removed under
    // a mounted pack makes each later read from it log a failure, and C73 reads the class cache C70's pack holds. The run removes
    // every pack's folder once no check needs a pack.
    private static readonly List<string> PackTempFolders = [];

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
            // A value a load left to its finalizer could be destroyed after the engine shut down (GXAML-81).
            ("C72", LoadLeavesNothingToTheFinalizer),
            // The preview server's text-to-scene step (GXAML-165).
            ("C74", PreviewInstantiatesAButton),
            ("C75", PreviewParseErrorGivesNoRoot),
            ("C76", PreviewResolvesRelativeReferences),
            ("C77", PreviewWritesNothingUnderRes),
            ("C78", PreviewLeavesNoCacheEntry),
            ("C79", PreviewStripsScriptsUnlessAllowed),
            ("C80", PreviewBuiltInScriptDoesNotRun),
            ("C81", PreviewDropsConnectionsOfStrippedNodes),
            ("C82", PreviewKeepsAnExplicitNullScript),
            // Instanced scenes and typed containers in a stripped preview (GXAML-177).
            ("C83", PreviewStripsAnInstancedScenesBuiltInScript),
            ("C84", PreviewStripsAnInstancedTscnsScriptFile),
            ("C85", PreviewStripsNestedInstancedScenes),
            ("C86", PreviewRetypesATypedArrayOfARemovedScript),
            ("C87", PreviewLeavesNoInstancedTempFiles),
            ("C88", PreviewStripsAnInstanceByItsUidsFile),
            ("C89", PreviewStripsAValueReferenceByItsUidsFile),
            ("C90", PreviewDropsConnectionsOfStrippedInstances),
            ("C91", PreviewReturnsOnAnInstanceCycle),
            ("C92", PreviewRefusesABinaryScene),
            ("C93", PreviewStripsAScriptByItsUidsFile),
            ("C94", PreviewStripsAScriptNamedByUid),
            ("C95", PreviewRefusesABinaryResourceScene),
            ("C96", PreviewRedirectsAResourceTypedInstance),
            ("C97", PreviewRefusesAnImportedScene),
            ("C98", PreviewKeepsAMissingBinaryResource),
            ("C99", () => PreviewRefusesAnUnreadableHeader("compressed.res", [.. "RSCC"u8, .. new byte[32]])),
            ("C100", () => PreviewRefusesAnUnreadableHeader("truncated.res", [.. "RSRC"u8, .. new byte[8]])),
            ("C101", () => PreviewRefusesAnUnreadableHeader("nomagic.res", [.. "XXXX"u8, .. new byte[36]])),
            ("C102", PreviewRefusesAnUpperCaseBinaryScene),
            // The preview's Control rectangles and source spans (GXAML-166).
            ("C105", PreviewRectsNeedATree),
            // Referenced resources, path references and path-typed containers in a stripped preview (GXAML-184).
            ("C106", PreviewStripsAResourcesBuiltInScript),
            ("C107", PreviewStripsAResourcesScriptFile),
            ("C108", PreviewStripsAnXres),
            ("C109", PreviewStripsResourcesAtAnyDepth),
            (
                "C110",
                () =>
                    Failures(
                        PreviewRetypesAPathTypedContainer("preview_typed_element.tscn", "list", Variant.Type.Array),
                        PreviewRetypesAPathTypedContainer("preview_typed_key.tscn", "map", Variant.Type.Dictionary),
                        PreviewRetypesAPathTypedContainer("preview_typed_value.tscn", "map", Variant.Type.Dictionary)
                    )
            ),
            ("C111", PreviewNullsAPathReferenceToAScript),
            ("C112", PreviewRefusesABinaryResource),
            ("C113", PreviewRefusesAPathValueToAScene),
            // Review of GXAML-184: instances, tag fields, script resources, Object values (GXAML-191).
            ("C114", PreviewRefusesAPathInstance),
            ("C115", PreviewDropsUnknownTagFields),
            ("C116", PreviewNullsAResourceThatIsAScript),
            ("C117", PreviewObjectValueScriptDoesNotRun),
            ("C118", PreviewClassifiesAPathValuesUidAndPath),
            ("C119", PreviewCopiesAResourceForATypedProperty),
            ("C120", PreviewKeepsAPathValueToAnInertFile),
            ("C121", PreviewNullsAPathValueByItsUid),
            // Classification by content, remaps, GDExtension and unknown extensions (GXAML-189).
            ("C122", PreviewRefusesABinarySceneBehindABaseExtension),
            ("C123", () => Failures(PreviewRefusesABinaryResourceBehindABaseExtension(), PreviewClassifiesABinaryScriptBehindABaseExtension())),
            ("C124", PreviewStripsATextSceneInATres),
            (
                "C125",
                () =>
                    Failures(
                        PreviewRefusesAnUnreadableHeader("compressed.theme", [.. "RSCC"u8, .. new byte[32]]),
                        PreviewRefusesAnUnreadableHeader("truncated.mesh", [.. "RSRC"u8, .. new byte[8]])
                    )
            ),
            ("C126", () => Failures(PreviewClassifiesByImportSidecar(), PreviewClassifiesByImportedTypeAndTarget())),
            ("C127", () => Failures(PreviewFollowsRemaps(), PreviewRefusesUnsafeRemapsOfOtherFiles())),
            ("C128", () => Failures(PreviewRefusesAGDExtensionLibrary(), PreviewRefusesADeclaredGDExtension())),
            ("C129", PreviewAsksClassDBBeforeTheMetadata),
            ("C130", PreviewRefusesAnUnknownExtension),
            // GXAML-189 review round 1: Godot's sidecar reading, import targets, front loaders, missing files.
            ("C131", () => Failures(PreviewReadsSidecarKeysAsGodotDoes(), PreviewRefusesSidecarTextGodotReadsOtherwise())),
            ("C132", () => Failures(PreviewFindsLoadingValuesByToken(), PreviewFindsEveryLoadingValue())),
            ("C133", PreviewLoadsThroughAnInvalidImport),
            ("C134", () => Failures(PreviewClassifiesImportTargetsFully(), PreviewRefusesAnImportTargetThatLeavesTheImporterFolder())),
            ("C135", () => Failures(PreviewClassifiesTheImporterBesideAFrontLoader(), PreviewClassifiesATypelessOrTiedImport())),
            ("C136", PreviewRefusesAMissingUnknownExtension),
            // With scripts off, a project that declares a global ResourceFormatLoader class gets no preview, and the script
            // extensions are a fixed table.
            ("C137", PreviewRefusesAProjectWithALoaderClass),
            // With scripts off, a placeholder's scene is loaded by the editor's instantiation, so it is stripped and copied
            // too, and one that cannot be stripped is refused (GXAML-195).
            ("C138", PreviewStripsAnInstancePlaceholdersScene),
            // The preview server's discovery file (GXAML-167).
            ("C139", PreviewServerWritesAndRemovesItsDiscoveryFile),
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
            // The preview's Control rectangles and source spans (GXAML-166).
            ("C103", PreviewRectsFollowTheTreeAndSpans),
            ("C104", PreviewRectsSelectTheInstanceElement),
            // The preview server, talked to over loopback while the frame loop pumps it (GXAML-167).
            ("C140", async () => Failures(await PreviewServerAuthenticatesTheFirstPing(), await PreviewServerCapsConnections())),
            ("C141", PreviewServerRefusesMalformedFrames),
            ("C142", PreviewServerRendersRects),
            ("C143", PreviewServerKeepsTheLatestRender),
        ];
        DeleteFolder(SavesFolder);
        int total = checks.Length + frameChecks.Length + 2;
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

            // Last, because a mounted pack cannot be unmounted and freezes res:// for the rest of the process. C73 mounts the
            // second pack and reads a file only it holds.
            passed += Report("C70", RefreshSeesAMountedPacksClasses) ? 1 : 0;
            passed += Report("C73", RawXresInAMountedPackHasItsScriptClass) ? 1 : 0;
        }
        finally
        {
            RemovePackTempFolders();
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

    // A failed load that keeps its temp file prunes the folder: an old kept file goes, a recent one stays, the old
    // backup-save leftovers of one go too, a name that only looks like one stays, and the error still names this load's own
    // file.
    private static string FailedLoadPrunesOldKeptFiles()
    {
        const string copyPath = OutFolder + "prune.xscn";
        string loaderTempFolder = AddonTempFolder.Get();
        static string Temp(string folder, string suffix) => Path.Combine(folder, $"{Guid.NewGuid():N}{suffix}");
        (string File, TimeSpan Age, bool Pruned)[] planted =
        [
            // The kept file's own name, old and recent.
            (Temp(loaderTempFolder, ".tscn"), TimeSpan.FromDays(2), true),
            (Temp(loaderTempFolder, ".tres"), TimeSpan.FromHours(1), false),
            // Godot's backup save renames the target to "<path>-XXXXXX" (unix, mkstemp) or writes "<path><ticks>.tmp" beside
            // it (windows), so a leftover carries the kept file's stem and a suffix; an old one is pruned like the kept file.
            (Temp(loaderTempFolder, ".tscn-AbC123"), TimeSpan.FromDays(2), true),
            (Temp(loaderTempFolder, ".tres1234567890.tmp"), TimeSpan.FromDays(2), true),
            (Temp(loaderTempFolder, ".tscn-XyZ789"), TimeSpan.FromHours(1), false),
            // Old names that reach for the same shape and must stay: no guid stem, another extension, an uppercase stem, a
            // five-character suffix, and no digits before ".tmp".
            (Path.Combine(loaderTempFolder, "notaguid.tscn-AbC123"), TimeSpan.FromDays(2), false),
            (Temp(loaderTempFolder, ".txt-AbC123"), TimeSpan.FromDays(2), false),
            (Path.Combine(loaderTempFolder, "0123456789ABCDEF0123456789ABCDEF.tscn-AbC123"), TimeSpan.FromDays(2), false),
            (Temp(loaderTempFolder, ".tscn-AbC12"), TimeSpan.FromDays(2), false),
            (Temp(loaderTempFolder, ".tscn.tmp"), TimeSpan.FromDays(2), false),
        ];
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            PlantFiles(planted);
            DeleteFolder(OutFolder);
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
            string holder = File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath));
            File.WriteAllText(
                ProjectSettings.GlobalizePath(copyPath),
                holder.Replace("</Control>", AbstractSubResource + "</Control>", StringComparison.Ordinal)
            );
            WarningLog log = LoadLogged(copyPath).Log;
            string[] kept = [.. XscnLoadRoute.TempPaths.Skip(tempPathsBeforeLoad).Where(File.Exists)];
            var failures = new List<string>(PruneFailures(planted))
            {
                kept.Length > 0 ? "" : "the failed load kept no temp file",
                kept.Any(file => Logged(log.Entries, Logger.ErrorType.Error, file))
                    ? ""
                    : $"no error names the kept file [{string.Join(", ", kept)}]",
            };
            return Failures([.. failures]);
        }
        finally
        {
            RemovePlantedFiles(planted);
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            DeleteFolder(OutFolder);
        }
    }

    // Each planted file, written empty and aged, so the prune's day-old cutoff decides it.
    private static void PlantFiles((string File, TimeSpan Age, bool Pruned)[] planted)
    {
        foreach ((string file, TimeSpan age, _) in planted)
        {
            File.WriteAllText(file, "");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow - age);
        }
    }

    // Each planted name's fate: the prune removes the ones it must and leaves the others.
    private static IEnumerable<string> PruneFailures((string File, TimeSpan Age, bool Pruned)[] planted) =>
        planted
            .Where(entry => File.Exists(entry.File) == entry.Pruned)
            .Select(entry => entry.Pruned ? $"the old file was not pruned: {entry.File}" : $"a file the prune must leave alone went: {entry.File}");

    private static void RemovePlantedFiles((string File, TimeSpan Age, bool Pruned)[] planted)
    {
        foreach ((string file, _, _) in planted)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    // A class the running engine has is never warning material: the bundled metadata can lack a class this engine registered,
    // and the file still loads through it.
    private static string KnownEngineClassIsNotWarned()
    {
        const string path = OutFolder + "known.xscn";
        string? engineClass = EngineOnlyClass();
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

    // A class the running engine has and the bundled metadata lacks, whose node the parser therefore flags GX0107.
    private static string? EngineOnlyClass()
    {
        var metadata = GodotMetadata.LoadBundled("4.7");
        return ClassDB
            .GetClassList()
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(name =>
                metadata.FindClassByCSharpName(name) is null
                && XscnParser.Parse(KnownScene(name), XscnDocumentKind.Scene, metadata).Diagnostics.Any(d => d.Code == XscnCodes.UnknownClass)
            );
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
        string temp = NewPackTempFolder();
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
        }
    }

    // A pack holds files that exist only in it, and Godot's FileAccess serves them; System.IO does not. The raw .xres goes to
    // the scratch folder and into the pack at a res:// path with no file on disk, so the root-tag reads behind
    // _GetResourceScriptClass and _GetResourceType have to go through FileAccess. It runs after C70, whose refresh put the
    // pack's global class in the snapshot the script is looked up in.
    private static string RawXresInAMountedPackHasItsScriptClass()
    {
        // C70's script and class: the pack's class cache already maps the script to the class.
        const string dlcScriptPath = "res://checks/dlc/dlc_checked.gd";
        const string dlcClass = "DlcCheckedResource";
        const string scriptedPath = "res://checks/dlc/only_in_pack.xres";
        const string nativePath = "res://checks/dlc/native_only_in_pack.xres";
        using var loader = new XresFormatLoader();
        string temp = NewPackTempFolder();
        string scriptedFile = Path.Combine(temp, "only_in_pack.xres");
        File.WriteAllText(scriptedFile, $"<Resource xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Script=\"{dlcScriptPath}\" />\n");
        string nativeFile = Path.Combine(temp, "native_only_in_pack.xres");
        File.WriteAllText(nativeFile, $"<Gradient xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" />\n");
        string mountFailure = MountPack(Path.Combine(temp, "raw.pck"), [(scriptedPath, scriptedFile), (nativePath, nativeFile)]);
        if (mountFailure.Length > 0)
        {
            return mountFailure;
        }

        string scriptClass = loader._GetResourceScriptClass(scriptedPath);
        string type = loader._GetResourceType(nativePath);
        return Failures(
            scriptClass == dlcClass ? "" : $"the pack's {scriptedPath} reports script class \"{scriptClass}\", not \"{dlcClass}\"",
            type == "Gradient" ? "" : $"the pack's {nativePath} reports type \"{type}\", not \"Gradient\""
        );
    }

    // A pack's sources are scratch files outside the project, kept until the run is done with every pack and a folder left behind
    // is worth a warning, not a failure.
    private static string NewPackTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gx-check-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        PackTempFolders.Add(folder);
        return folder;
    }

    private static void RemovePackTempFolders()
    {
        foreach (string folder in PackTempFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"the checks could not remove their temp folder {folder}: {exception.Message}");
            }
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

    // GodotSharp registers each Variant, StringName, NodePath, Array and Dictionary it makes in DisposablesTracker under a
    // short weak reference (modules/mono/glue/GodotSharp/GodotSharp/Core/DisposablesTracker.cs:65-80), and each GodotObject
    // wrapper under the table beside it (DisposablesTracker.cs:62). One a GC finds unreachable is left to its finalizer,
    // which may run while the engine shuts down and crash the process (GXAML-81), so a load must dispose everything it
    // makes. Godot's own text loader, called the same way, leaves none.
    private static string LoadLeavesNothingToTheFinalizer()
    {
        // Internals of GodotSharp: a Godot version that renames them fails here by name rather than counting nothing.
        Type? tracker = typeof(Godot.GodotObject).Assembly.GetType("Godot.DisposablesTracker");
        if (
            tracker?.GetProperty("OtherInstances", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            is not ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked
        )
        {
            return "Godot.DisposablesTracker.OtherInstances is missing or not a ConcurrentDictionary<WeakReference<IDisposable>, byte>";
        }

#if TOOLS
        if (
            tracker.GetProperty("GodotObjectInstances", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            is not ConcurrentDictionary<WeakReference<Godot.GodotObject>, byte> objects
        )
        {
            return "Godot.DisposablesTracker.GodotObjectInstances is missing or not a ConcurrentDictionary<WeakReference<GodotObject>, byte>";
        }
#endif

        using var xres = new XresFormatLoader();
        List<string> failures = [.. new[] { ContainerPath, ContainedPath, ScriptedGdPath }.Select(path => LoadFailure(tracked, path))];
        failures.Add(LeftToTheFinalizer(tracked, "rebuilding the global class snapshot", XresFormatLoader.RefreshGlobalClasses).Failure());
        failures.AddRange(HandlesTypeFailures(tracked, xres));
        failures.Add(RootClassFailure(tracked, xres));
        failures.Add(RenameFailure(tracked));
        failures.Add(KnownToEngineFailure(tracked));
#if TOOLS
        failures.Add(ExportFailure(objects));
#endif
        return string.Join("; ", failures.Where(failure => failure.Length > 0));
    }

    // Each loader's _HandlesType is called directly: an engine call goes through the generated dispatch, whose own StringName
    // parameter is Godot's to dispose, not the addon's. An engine class and a global class run both of its paths.
    private static List<string> HandlesTypeFailures(ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked, XresFormatLoader xres)
    {
        using var xscn = new XscnFormatLoader();
        using var type = new StringName("Resource");
        using var global = new StringName("CheckedResource");
        (string Loader, ResourceFormatLoader Instance)[] loaders = [(nameof(XresFormatLoader), xres), (nameof(XscnFormatLoader), xscn)];
        var failures = new List<string>(4);
        foreach ((string loader, ResourceFormatLoader instance) in loaders)
        {
            foreach (StringName asked in new[] { type, global })
            {
                string askedText = $"{loader}._HandlesType(\"{asked}\")";
                failures.Add(LeftToTheFinalizer(tracked, askedText, () => instance._HandlesType(asked)).Failure());
            }
        }

        return [.. failures.Where(failure => failure.Length > 0)];
    }

    private static string LoadFailure(ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked, string path)
    {
        Resource? loaded = null;
        Measurement measured = LeftToTheFinalizer(
            tracked,
            $"loading {path}",
            () => loaded = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore)
        );
        GC.KeepAlive(loaded);
        return loaded is null ? $"{path} did not load" : measured.Failure();
    }

    // The root class read through ClassDB, which the loader asks for before it parses: a read that never runs answers
    // "Resource" on an unreadable root, so only the fixture's own root class proves the tag was read.
    private static string RootClassFailure(ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked, XresFormatLoader xres)
    {
        const string expected = "Theme";
        string answer = "";
        string action = $"reading {ContainerPath}'s root class";
        Measurement measured = LeftToTheFinalizer(tracked, action, () => answer = xres._GetResourceType(ContainerPath));
        return measured.Failure() + (answer == expected ? "" : $"{ContainerPath}'s root class is \"{answer}\", not \"{expected}\"");
    }

    // A rename copies each pair's Variants out of the caller's dictionary, and the copies are the addon's to dispose
    // (GXAML-81). The caller's dictionary is made before the measurement, so only the copies can be counted.
    private static string RenameFailure(ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked)
    {
        const string path = OutFolder + "renamed.xscn";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), File.ReadAllText(ProjectSettings.GlobalizePath(HolderPath)));
            using var loader = new XscnFormatLoader();
            using var renames = new Godot.Collections.Dictionary { [ThemePath] = CasesPath + "moved.xres" };
            Error error = Error.Ok;
            string action = $"renaming {path}";
            Measurement measured = LeftToTheFinalizer(tracked, action, () => error = loader._RenameDependencies(path, renames));
            return measured.Failure() + (error == Error.Ok ? "" : $"renaming {path} returned {error}");
        }
        finally
        {
            DeleteFolder(OutFolder);
        }
    }

    // A GX0107 names a class the bundled metadata lacks; loading it is the only route to the check that the running engine
    // has it, which is where the load makes a StringName of the tag (GXAML-81).
    private static string KnownToEngineFailure(ConcurrentDictionary<WeakReference<IDisposable>, byte> tracked)
    {
        string? engineClass = EngineOnlyClass();
        if (engineClass is null)
        {
            return "setup: every ClassDB class is in the bundled metadata";
        }

        string path = OutFolder + "known.xscn";
        string action = $"loading a scene naming {engineClass}";
        DeleteFolder(OutFolder);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutFolder));
        int tempPathsBeforeLoad = XscnLoadRoute.TempPaths.Count;
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), KnownScene(engineClass));
            Resource? loaded = null;
            Measurement measured = LeftToTheFinalizer(tracked, action, () => loaded = ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore));
            GC.KeepAlive(loaded);
            return measured.Failure() + (loaded is null ? $"{path} did not load" : "");
        }
        finally
        {
            DeleteKeptTempFiles(tempPathsBeforeLoad);
            DeleteFolder(OutFolder);
        }
    }

#if TOOLS
    // The ignored-cache load's resource wrapper is released in the export's own finally (GXAML-81), so it is counted in the
    // GodotObject table. The walk keeps the wrappers of a file's sub-resources by design, so the measured file has none.
    // The first export is unmeasured: wrappers it registers are meant to live (an engine singleton's, for one), and a wrapper
    // this check never disposes would otherwise read as the export's leak. Only the second export is counted.
    private static string ExportFailure(ConcurrentDictionary<WeakReference<Godot.GodotObject>, byte> objects)
    {
        using var plugin = new XscnExportPlugin();
        string? warmUp = plugin.ExportBinary(BoxPath, "res");
        string? reason = "";
        string action = $"exporting {BoxPath} as Godot binary";
        Measurement measured = StillRegistered(objects, action, () => reason = plugin.ExportBinary(BoxPath, "res"));
        return measured.Failure() + (warmUp is null && reason is null ? "" : $"the export of {BoxPath} failed: {warmUp ?? reason}");
    }
#endif

    // What one measured action left the finalizer, or why the no-GC region that keeps the count honest could not be held.
    private readonly record struct Measurement(int Left, string Action, string RegionFailure)
    {
        // The failure line for the measured action, or "" when it held its region and left nothing.
        public string Failure() =>
            RegionFailure.Length > 0 ? RegionFailure
            : Left != 0 ? $"{Action} left {Left} Godot disposables to the finalizer"
            : "";
    }

    // The tracked entries the action added that one GC finds unreachable: a Variant, StringName, NodePath, Array or
    // Dictionary is tracked while it lives and untracked when disposed, so a dead target is one nobody disposed. No target
    // is read before the collect, so nothing on this stack keeps one alive.
    private static Measurement LeftToTheFinalizer<T>(ConcurrentDictionary<WeakReference<T>, byte> tracked, string action, Action run)
        where T : class =>
        Measured(
            tracked,
            action,
            run,
            made =>
            {
                GC.Collect();
                int left = made.Count(key => !key.TryGetTarget(out _));
                GC.WaitForPendingFinalizers();
                return left;
            }
        );

#if TOOLS
    // The tracked entries the action added and did not untrack: a GodotObject wrapper is untracked by its Dispose, so an
    // entry it leaves is a wrapper it did not dispose. The rule does not rest on the native side keeping the wrapper alive:
    // Godot swaps the strong handle for a weak one once only the managed side holds an object
    // (modules/mono/csharp_script.cpp:1300-1320). A wrapper meant to live is untracked only by a Dispose the action never
    // makes, which is why the export measurement is warmed up before it counts.
    private static Measurement StillRegistered<T>(ConcurrentDictionary<WeakReference<T>, byte> tracked, string action, Action run)
        where T : class => Measured(tracked, action, run, made => made.Count);
#endif

    // Runs the action inside a no-GC region: a GC during it could finalize a leaked entry, whose disposal untracks it before
    // the count, so an action that spends the region's budget fails instead of under-counting.
    private static Measurement Measured<T>(
        ConcurrentDictionary<WeakReference<T>, byte> tracked,
        string action,
        Action run,
        Func<List<WeakReference<T>>, int> count
    )
        where T : class
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = new HashSet<WeakReference<T>>(tracked.Keys);
        if (!GC.TryStartNoGCRegion(NoGcRegionBytes))
        {
            return new Measurement(0, action, $"{action}: no no-GC region of {NoGcRegionBytes / (1024 * 1024)} MB could be started");
        }

        List<WeakReference<T>> made = [];
        string regionFailure = "";
        try
        {
            RunNotInlined(run);
            // Read inside the region: an entry the action untracked is already gone, and a leak cannot be finalized away.
            made = [.. tracked.Keys.Where(key => !before.Contains(key))];
        }
        finally
        {
            try
            {
                GC.EndNoGCRegion();
            }
            catch (InvalidOperationException exception)
            {
                regionFailure = $"{action}: a GC ran inside its no-GC region of {NoGcRegionBytes / (1024 * 1024)} MB: {exception.Message}";
            }
        }

        return new Measurement(regionFailure.Length > 0 ? 0 : count(made), action, regionFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunNotInlined(Action action) => action();

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

    // Previews the text and inspects its root, which is freed afterwards; a null root fails with the preview's problems.
    private static string WithPreview(string text, string resPath, bool scriptsRequested, Func<Node, string> inspect)
    {
        PreviewScene preview = XscnPreviewScene.Instantiate(text, resPath, scriptsRequested);
        if (preview.Root is not { } root)
        {
            return $"the preview has no root: {PreviewProblems(preview)}";
        }

        try
        {
            return inspect(root);
        }
        finally
        {
            root.Free();
        }
    }

    private static string PreviewProblems(PreviewScene preview) =>
        string.Join("; ", preview.Diagnostics.Select(d => $"{d.Code} {d.Message}").Concat(preview.Issues.Select(i => $"{i.Code} {i.Message}")));

    // A Button scene's text instantiates with its Button and no diagnostics.
    private static string PreviewInstantiatesAButton()
    {
        PreviewScene preview = XscnPreviewScene.Instantiate(CleanScene, PreviewPath, scriptsRequested: false);
        try
        {
            return Failures(
                preview.Diagnostics.IsEmpty ? "" : $"diagnostics: {PreviewProblems(preview)}",
                preview.Root switch
                {
                    null => "the preview has no root",
                    var root => root.GetNodeOrNull("Ok") is Button ? "" : "the root has no Button named Ok",
                }
            );
        }
        finally
        {
            preview.Root?.Free();
        }
    }

    // A text that does not parse gives its GX error and no root.
    private static string PreviewParseErrorGivesNoRoot()
    {
        PreviewScene preview = XscnPreviewScene.Instantiate(FailedParseScene, PreviewPath, scriptsRequested: false);
        try
        {
            bool hasError = preview.Diagnostics.Any(d => d.Severity == XscnSeverity.Error && d.Code.StartsWith("GX", StringComparison.Ordinal));
            return Failures(
                hasError ? "" : $"the diagnostics hold no GX error: [{PreviewProblems(preview)}]",
                preview.Root is null ? "" : $"the preview returned a {preview.Root.GetClass()} root, not null"
            );
        }
        finally
        {
            preview.Root?.Free();
        }
    }

    // A relative reference in the text resolves against the document's res:// path, not the temp file's folder. A stripped
    // preview loads every referenced text resource from its stripped copy in the temp folder, so the box is known by its
    // content, the relative box.xres's blue, and by a path outside res://, not by its original path.
    private static string PreviewResolvesRelativeReferences() =>
        WithPreview(
            PreviewRelativeScene,
            RelativeHolderPath,
            scriptsRequested: false,
            root =>
            {
                const string expected = RelativeCasesPath + "box.xres";
                var blue = new Color(0, 0, 1, 1);
                using Variant box = root.GetMeta("box", default(Variant));
                return Failures(
                    root.GetNodeOrNull("Added") is null ? "the root has no node Added, so the file was read, not the text" : "",
                    box.Obj switch
                    {
                        StyleBoxFlat flat when flat.BgColor != blue => $"metadata/box's BgColor is {flat.BgColor}, not {expected}'s {blue}",
                        StyleBoxFlat flat when flat.ResourcePath.StartsWith("res://", StringComparison.Ordinal) =>
                            $"metadata/box was loaded from \"{flat.ResourcePath}\", not from a stripped copy",
                        StyleBoxFlat => "",
                        Resource other => $"metadata/box is a {other.GetClass()} at \"{other.ResourcePath}\", not the StyleBoxFlat of {expected}",
                        _ => "metadata/box holds no resource",
                    }
                );
            }
        );

    // A preview writes nothing under res://, its only temp file is in the addon's temp folder, and the file at the document's
    // path keeps its bytes.
    private static string PreviewWritesNothingUnderRes()
    {
        string userFile = ProjectSettings.GlobalizePath(RelativeHolderPath);
        byte[] bytesBefore = File.ReadAllBytes(userFile);
        string[] listingBefore = ResListing();
        bool recording = XscnLoadRoute.RecordTempPaths;
        int tempPathsBeforePreview = XscnLoadRoute.TempPaths.Count;
        string failure;
        string[] tempPaths;
        try
        {
            XscnLoadRoute.RecordTempPaths = true;
            failure = WithPreview(PreviewRelativeScene, RelativeHolderPath, scriptsRequested: false, _ => "");
            tempPaths = [.. XscnLoadRoute.TempPaths.Skip(tempPathsBeforePreview)];
        }
        finally
        {
            // The run records every temp path from its start, and later checks count from the queue, so it is left as found.
            XscnLoadRoute.RecordTempPaths = recording;
        }

        string[] listingAfter = ResListing();
        string[] added = [.. listingAfter.Except(listingBefore, StringComparer.Ordinal)];
        string[] gone = [.. listingBefore.Except(listingAfter, StringComparer.Ordinal)];
        string tempFolder = AddonTempFolder.Get().TrimEnd('/') + "/";
        string resFolder = ProjectSettings.GlobalizePath("res://").Replace('\\', '/').TrimEnd('/') + "/";
        string[] misplaced =
        [
            .. tempPaths.Where(path =>
                !path.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(resFolder, StringComparison.OrdinalIgnoreCase)
            ),
        ];
        return Failures(
            failure,
            added.Length == 0 && gone.Length == 0
                ? ""
                : $"res:// changed: new or touched [{string.Join(", ", added)}], gone [{string.Join(", ", gone)}]",
            File.ReadAllBytes(userFile).AsSpan().SequenceEqual(bytesBefore) ? "" : $"{RelativeHolderPath}'s bytes changed",
            tempPaths.Length == 0 ? "the preview recorded no temp file" : "",
            misplaced.Length == 0 ? "" : $"temp files outside {tempFolder} or under {resFolder}: [{string.Join(", ", misplaced)}]"
        );
    }

    // Every file under res://, with its last write time.
    private static string[] ResListing()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        return
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => $"{file} {File.GetLastWriteTimeUtc(file).Ticks}")
                .Order(StringComparer.Ordinal),
        ];
    }

    // The preview's sub-resources stay out of the cache while its root is alive.
    private static string PreviewLeavesNoCacheEntry()
    {
        XscnParseResult parsed = XscnParser.Parse(PreviewInlineScene, XscnDocumentKind.Scene, GodotMetadata.LoadBundled("4.7"));
        string[] subPaths = [.. XscnLoading.ToTscn(parsed.Document, PreviewPath).Document.SubResources.Select(sub => $"{PreviewPath}::{sub.Id}")];
        if (subPaths.Length == 0)
        {
            return "the inline scene converts with no sub-resource";
        }

        if (subPaths.FirstOrDefault(ResourceLoader.HasCached) is { } before)
        {
            return $"{before} was cached before the preview";
        }

        return WithPreview(
            PreviewInlineScene,
            PreviewPath,
            scriptsRequested: false,
            _ => Failures([.. subPaths.Select(path => ResourceLoader.HasCached(path) ? $"{path} is cached" : "")])
        );
    }

    // The root's script is stripped, its Control property kept, unless the request and the project setting both allow it.
    private static string PreviewStripsScriptsUnlessAllowed()
    {
        static string Stripped(Node root)
        {
            using Variant script = root.GetScript();
            return Failures(
                script.VariantType == Variant.Type.Nil ? "" : "the root has a script",
                root is Control { TooltipText: "kept" } ? "" : "the root's TooltipText is not \"kept\""
            );
        }

        static string Scripted(Node root)
        {
            using Variant script = root.GetScript();
            return script.VariantType == Variant.Type.Nil ? "the root has no script" : "";
        }

        using var previous = new PreviewSettingScope();
        string notRequested = WithPreview(PreviewScriptedScene, PreviewPath, scriptsRequested: false, Stripped);
        ProjectSettings.SetSetting(PreviewScriptsSetting, false);
        string settingOff = WithPreview(PreviewScriptedScene, PreviewPath, scriptsRequested: true, Stripped);
        // A stripped preview loads no script either: the script's file stays out of the cache.
        bool scriptLoaded = ResourceLoader.HasCached(PreviewRootScriptPath);
        ProjectSettings.SetSetting(PreviewScriptsSetting, true);
        string bothOn = WithPreview(PreviewScriptedScene, PreviewPath, scriptsRequested: true, Scripted);
        return Failures(
            notRequested.Length == 0 ? "" : $"not requested: {notRequested}",
            settingOff.Length == 0 ? "" : $"requested, setting off: {settingOff}",
            scriptLoaded ? $"{PreviewRootScriptPath} is cached after the stripped previews" : "",
            bothOn.Length == 0 ? "" : $"requested, setting on: {bothOn}",
            EditorTypedScriptExtFailure()
        );
    }

    // In the editor the conversion types a .gd ext_resource GDScript, not Script; the strip drops it by class all the same.
    private static string EditorTypedScriptExtFailure()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "GDScript",
                    Path = PreviewRootScriptPath,
                    Id = "1_gdscr",
                },
                // Held only in metadata, so only its class can remove it.
                new TscnExtResource
                {
                    Type = "GDScript",
                    Path = CasesPath + "preview_signals.gd",
                    Id = "2_gdscr",
                },
            ],
            Nodes =
            [
                new TscnNode
                {
                    Name = "Root",
                    Type = "Control",
                    Properties =
                    [
                        new TscnProperty("script", new GodotResourceRef(GodotRefKind.External, "1_gdscr")),
                        new TscnProperty("metadata/s", new GodotResourceRef(GodotRefKind.External, "2_gdscr")),
                    ],
                },
            ],
        };
        TscnDocument stripped = XscnPreviewScene.WithoutScripts(document);
        string[] properties = [.. stripped.Nodes[0].Properties.Select(property => property.Name)];
        return Failures(
            stripped.ExtResources.IsEmpty
                ? ""
                : $"the strip kept the ext_resources [{string.Join(", ", stripped.ExtResources.Select(ext => $"{ext.Type} {ext.Path}"))}]",
            properties.SequenceEqual(["metadata/s"], StringComparer.Ordinal)
                ? ""
                : $"the root's properties after the strip are [{string.Join(", ", properties)}], not [metadata/s]",
            stripped.Nodes[0].Properties.FirstOrDefault(property => property.Name == "metadata/s")?.Value is GodotNull
                ? ""
                : "the root's metadata/s is not null after the strip"
        );
    }

    // An explicit null script on an instance detaches the instanced scene's script, and stays in a stripped preview: the
    // instance has no script and keeps its connections.
    private static string PreviewKeepsAnExplicitNullScript() =>
        WithPreview(
            PreviewNullScriptScene,
            PreviewPath,
            scriptsRequested: false,
            root =>
            {
                if (root.GetNodeOrNull("Inst") is not { } instance)
                {
                    return "the root has no node Inst";
                }

                using Variant script = instance.GetScript();
                return Failures(
                    script.VariantType == Variant.Type.Nil ? "" : "the instance has its scene's script back",
                    root.GetNodeOrNull("Plain") is { } plain && IncomingConnections(plain) == 1 ? "" : "Plain does not have its 1 incoming connection"
                );
            }
        );

    // The typed list's cast returns its untyped Array, which can be disposed.
    private static int IncomingConnections(Node node)
    {
        using var connections = (Godot.Collections.Array)node.GetIncomingConnections();
        return connections.Count;
    }

    // A built-in @tool script's static initializer runs only when the request and the setting both allow scripts.
    private static string PreviewBuiltInScriptDoesNotRun()
    {
        using var previous = new PreviewSettingScope();
        Godot.GodotObject engine = Engine.Singleton;
        engine.RemoveMeta(PreviewRanMeta);
        try
        {
            ProjectSettings.SetSetting(PreviewScriptsSetting, false);
            string stripped = WithPreview(PreviewBuiltInScriptScene, PreviewPath, scriptsRequested: true, _ => "");
            bool ranStripped = engine.HasMeta(PreviewRanMeta);
            engine.RemoveMeta(PreviewRanMeta);
            ProjectSettings.SetSetting(PreviewScriptsSetting, true);
            string allowed = WithPreview(PreviewBuiltInScriptScene, PreviewPath, scriptsRequested: true, _ => "");
            bool ranAllowed = engine.HasMeta(PreviewRanMeta);
            return Failures(
                stripped,
                ranStripped ? "the built-in script's _static_init ran in a stripped preview" : "",
                allowed,
                ranAllowed ? "" : "the built-in script's _static_init did not run with scripts allowed"
            );
        }
        finally
        {
            engine.RemoveMeta(PreviewRanMeta);
        }
    }

    // A connection from a stripped node's script signal, and one to a stripped node's script method, are dropped with the
    // scripts, and kept when scripts are allowed.
    private static string PreviewDropsConnectionsOfStrippedNodes()
    {
        static Func<Node, string> Expect(int each, int toEmitter) =>
            root =>
                Failures(
                    root.GetNodeOrNull("Plain") is { } plain && IncomingConnections(plain) == each
                        ? ""
                        : $"Plain does not have {each} incoming connection(s)",
                    IncomingConnections(root) == each ? "" : $"the root does not have {each} incoming connection(s)",
                    // Named "./Emitter" and "Plain/../Emitter" in the text, so the strip must match the path Godot simplifies.
                    root.GetNodeOrNull("Emitter")
                        is { } emitter
                    && IncomingConnections(emitter) == toEmitter
                        ? ""
                        : $"Emitter does not have {toEmitter} incoming connection(s)"
                );

        using var previous = new PreviewSettingScope();
        ProjectSettings.SetSetting(PreviewScriptsSetting, false);
        string stripped = WithPreview(PreviewSignalsScene, PreviewPath, scriptsRequested: true, Expect(0, 0));
        ProjectSettings.SetSetting(PreviewScriptsSetting, true);
        string allowed = WithPreview(PreviewSignalsScene, PreviewPath, scriptsRequested: true, Expect(1, 2));
        return Failures(stripped.Length == 0 ? "" : $"stripped: {stripped}", allowed.Length == 0 ? "" : $"allowed: {allowed}");
    }

    // A text whose root instances the scene at the path as Inst.
    private static string InstancingScene(string scenePath) =>
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + $"  <x:Instance Scene=\"{scenePath}\" Name=\"Inst\" />\n"
        + "</Control>\n";

    private static string HasNode(Node root, string path) => root.GetNodeOrNull(path) is null ? $"the root has no node {path}" : "";

    // An instanced .xscn's built-in @tool script runs its static initializer only when the request and the setting both
    // allow scripts; stripped, the instance is still there.
    private static string PreviewStripsAnInstancedScenesBuiltInScript()
    {
        string text = InstancingScene(CasesPath + "preview_inst_builtin.xscn");
        using var previous = new PreviewSettingScope();
        Godot.GodotObject engine = Engine.Singleton;
        engine.RemoveMeta(PreviewInstancedRanMeta);
        try
        {
            ProjectSettings.SetSetting(PreviewScriptsSetting, false);
            string stripped = WithPreview(text, PreviewPath, scriptsRequested: true, root => HasNode(root, "Inst"));
            bool ranStripped = engine.HasMeta(PreviewInstancedRanMeta);
            engine.RemoveMeta(PreviewInstancedRanMeta);
            ProjectSettings.SetSetting(PreviewScriptsSetting, true);
            string allowed = WithPreview(text, PreviewPath, scriptsRequested: true, _ => "");
            bool ranAllowed = engine.HasMeta(PreviewInstancedRanMeta);
            return Failures(
                stripped.Length == 0 ? "" : $"stripped: {stripped}",
                ranStripped ? "the instanced scene's built-in script ran its _static_init in a stripped preview" : "",
                allowed.Length == 0 ? "" : $"allowed: {allowed}",
                ranAllowed ? "" : "the instanced scene's built-in script did not run its _static_init with scripts allowed"
            );
        }
        finally
        {
            engine.RemoveMeta(PreviewInstancedRanMeta);
        }
    }

    // An instanced .tscn's @tool script file is neither run nor loaded in a stripped preview.
    private static string PreviewStripsAnInstancedTscnsScriptFile()
    {
        Godot.GodotObject engine = Engine.Singleton;
        engine.RemoveMeta(PreviewInstancedFileRanMeta);
        try
        {
            string stripped = WithPreview(
                InstancingScene(CasesPath + "preview_inst_script.tscn"),
                PreviewPath,
                scriptsRequested: false,
                root => HasNode(root, "Inst")
            );
            return Failures(
                stripped,
                engine.HasMeta(PreviewInstancedFileRanMeta) ? "the instanced scene's script ran its _static_init in a stripped preview" : "",
                ResourceLoader.HasCached(PreviewInstancedScriptPath) ? $"{PreviewInstancedScriptPath} is cached after the stripped preview" : ""
            );
        }
        finally
        {
            engine.RemoveMeta(PreviewInstancedFileRanMeta);
        }
    }

    // A scene instanced by an instanced scene is stripped too, and the instance tree stays whole.
    private static string PreviewStripsNestedInstancedScenes()
    {
        Godot.GodotObject engine = Engine.Singleton;
        engine.RemoveMeta(PreviewDeepRanMeta);
        try
        {
            string stripped = WithPreview(
                InstancingScene(CasesPath + "preview_deep_a.xscn"),
                PreviewPath,
                scriptsRequested: false,
                root => HasNode(root, "Inst/B")
            );
            return Failures(stripped, engine.HasMeta(PreviewDeepRanMeta) ? "B's built-in script ran its _static_init in a stripped preview" : "");
        }
        finally
        {
            engine.RemoveMeta(PreviewDeepRanMeta);
        }
    }

    // A typed array whose element type is a removed script loads with the array kept, and the script is not loaded.
    private static string PreviewRetypesATypedArrayOfARemovedScript()
    {
        string stripped = WithPreview(
            PreviewTypedArrayScene,
            PreviewPath,
            scriptsRequested: false,
            root =>
            {
                using Variant list = root.GetMeta("list", default(Variant));
                return list.VariantType == Variant.Type.Array ? "" : $"metadata/list is a {list.VariantType}, not an Array";
            }
        );
        return Failures(
            stripped,
            ResourceLoader.HasCached(PreviewTypedScriptPath) ? $"{PreviewTypedScriptPath} is cached after the stripped preview" : ""
        );
    }

    // C83 to C86's and C107's stripped previews leave no .tscn or .tres copy in the addon's temp folder and write nothing
    // under res://. The folder is staged for the check, so another process's temp files do not count.
    private static string PreviewLeavesNoInstancedTempFiles()
    {
        string[] texts =
        [
            InstancingScene(CasesPath + "preview_inst_builtin.xscn"),
            InstancingScene(CasesPath + "preview_inst_script.tscn"),
            InstancingScene(CasesPath + "preview_deep_a.xscn"),
            PreviewTypedArrayScene,
            ReferencingResource(CasesPath + "preview_res_script.tres"),
        ];
        string staged = Path.Combine(Path.GetTempPath(), $"godot-xaml-c87-{Guid.NewGuid():N}");
        string[] resBefore = ResListing();
        AddonTempFolder.ResetForChecks(staged);
        try
        {
            string folder = AddonTempFolder.Get();
            string[] Copies() => [.. Directory.GetFiles(folder, "*.tscn"), .. Directory.GetFiles(folder, "*.tres")];
            string[] before = Copies();
            string failure = Failures([.. texts.Select(text => WithPreview(text, PreviewPath, scriptsRequested: false, _ => ""))]);
            // C91's cycle: Godot refuses the load, and the load route keeps its own temp file on purpose; only the preview's
            // copies must be gone.
            int tempPathsBeforeCycle = XscnLoadRoute.TempPaths.Count;
            XscnPreviewScene.Instantiate(InstancingScene(CasesPath + "preview_cycle_a.xscn"), PreviewPath, scriptsRequested: false).Root?.Free();
            DeleteKeptTempFiles(tempPathsBeforeCycle);
            string[] left = [.. Copies().Except(before, StringComparer.Ordinal)];
            string[] resAfter = ResListing();
            return Failures(
                failure,
                left.Length == 0 ? "" : $"temp scenes left in {folder}: [{string.Join(", ", left)}]",
                resAfter.SequenceEqual(resBefore, StringComparer.Ordinal)
                    ? ""
                    : $"res:// changed: new or touched [{string.Join(", ", resAfter.Except(resBefore, StringComparer.Ordinal))}]"
            );
        }
        finally
        {
            AddonTempFolder.ResetForChecks(null);
            if (Directory.Exists(staged))
            {
                Directory.Delete(staged, recursive: true);
            }
        }
    }

    // C88 and C89's declared path: a plain resource with no script, which the texts' x:Uids entries map to a scripted scene.
    private const string PreviewPlainPath = CasesPath + "preview_plain.tres";

    // A text whose x:Uids gives the plain resource's path the scene's uid, so Godot loads the scene where the text names the
    // plain resource; the body follows the table.
    private static string UidMappedScene(string scenePath, string body) =>
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Uids>\n"
        + $"    <x:Uid Path=\"{PreviewPlainPath}\" Value=\"{UidTextOf(scenePath)}\" />\n"
        + "  </x:Uids>\n"
        + body
        + "</Control>\n";

    // Runs the preview with the engine meta cleared before and after; fails with the preview's failure, or when the meta
    // was set, which the named script's static initializer does.
    private static string WithoutMarker(string meta, string script, Func<string> preview)
    {
        Godot.GodotObject engine = Engine.Singleton;
        engine.RemoveMeta(meta);
        try
        {
            string failure = preview();
            return Failures(failure, engine.HasMeta(meta) ? $"{script} ran its _static_init in a stripped preview" : "");
        }
        finally
        {
            engine.RemoveMeta(meta);
        }
    }

    // An instance whose declared path is a plain resource but whose uid names a scripted scene is stripped: the file Godot
    // loads is the uid's.
    private static string PreviewStripsAnInstanceByItsUidsFile()
    {
        const string scene = CasesPath + "preview_uid_inst.xscn";
        string text = UidMappedScene(scene, $"  <x:Instance Scene=\"{PreviewPlainPath}\" Name=\"I\" />\n");
        return WithoutMarker(
            "gx_preview_uid_inst",
            $"{scene}'s built-in script",
            () => WithPreview(text, PreviewPath, scriptsRequested: false, root => HasNode(root, "I"))
        );
    }

    // A metadata value whose declared path is a plain resource but whose uid names a scripted scene is stripped too.
    private static string PreviewStripsAValueReferenceByItsUidsFile()
    {
        const string scene = CasesPath + "preview_uid_meta.xscn";
        string text = UidMappedScene(scene, $"  <x:Prop Name=\"metadata/s\" Value='Resource(\"{PreviewPlainPath}\")' />\n");
        return WithoutMarker(
            "gx_preview_uid_meta",
            $"{scene}'s built-in script",
            () => WithPreview(text, PreviewPath, scriptsRequested: false, _ => "")
        );
    }

    // A connection from an instance's script signal is dropped with the instanced scene's script, and so is one from a
    // scripted node inside an instance under a parent; Godot logs no error.
    private static string PreviewDropsConnectionsOfStrippedInstances()
    {
        const string text =
            "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
            + "  <x:Instance Scene=\""
            + CasesPath
            + "preview_scripted.xscn\" Name=\"Inst\">\n"
            + "    <x:Connect Signal=\"poked\" To=\"Panel\" Method=\"hide\" />\n"
            + "  </x:Instance>\n"
            + "  <Control Name=\"Holder\">\n"
            + "    <x:Instance Scene=\""
            + CasesPath
            + "preview_conn_inner.xscn\" Name=\"Inner\">\n"
            + "      <x:Override Path=\"Emitter\">\n"
            + "        <x:Connect Signal=\"poked\" To=\"Panel\" Method=\"show\" />\n"
            + "      </x:Override>\n"
            + "    </x:Instance>\n"
            + "  </Control>\n"
            + "  <Control Name=\"Panel\" />\n"
            + "</Control>\n";
        var log = new WarningLog();
        OS.AddLogger(log);
        string failure;
        try
        {
            failure = WithPreview(
                text,
                PreviewPath,
                scriptsRequested: false,
                root =>
                    root.GetNodeOrNull("Panel") is { } panel && IncomingConnections(panel) == 0
                        ? ""
                        : "Panel is missing or has an incoming connection"
            );
        }
        finally
        {
            OS.RemoveLogger(log);
        }

        (Logger.ErrorType Type, string Text)[] errors = [.. log.Entries.Where(entry => entry.Type == Logger.ErrorType.Error)];
        return Failures(failure, errors.Length == 0 ? "" : $"Godot logged errors: {LogText(errors)}");
    }

    // A cycle of instanced scenes (A instances B, which instances A) returns from the preview, root or none, with no stack
    // overflow and no hang: either would end the run before DONE.
    private static string PreviewReturnsOnAnInstanceCycle()
    {
        int tempPathsBeforePreview = XscnLoadRoute.TempPaths.Count;
        PreviewScene preview = XscnPreviewScene.Instantiate(
            InstancingScene(CasesPath + "preview_cycle_a.xscn"),
            PreviewPath,
            scriptsRequested: false
        );
        preview.Root?.Free();
        // A failed load keeps its temp file for inspection; this one is expected.
        DeleteKeptTempFiles(tempPathsBeforePreview);
        return "";
    }

    // A binary scene cannot be stripped: the preview gives no root and a GXC0121 error.
    private static string PreviewRefusesABinaryScene()
    {
        const string scenePath = "user://gxaml-preview-binary.scn";
        var node = new Control { Name = "Binary" };
        using var packed = new PackedScene();
        Error packError = packed.Pack(node);
        node.Free();
        Error saved = packError == Error.Ok ? ResourceSaver.Save(packed, scenePath) : packError;
        if (saved != Error.Ok)
        {
            return $"setup: packing and saving {scenePath} gave {saved}";
        }

        try
        {
            PreviewScene preview = XscnPreviewScene.Instantiate(InstancingScene(scenePath), PreviewPath, scriptsRequested: false);
            preview.Root?.Free();
            bool refused = preview.Issues.Any(issue =>
                issue.Code == ConversionCodes.InstanceNotStripped && issue.Severity == ConversionSeverity.Error
            );
            return Failures(
                preview.Root is null ? "" : "the preview gave a root",
                refused ? "" : $"the issues hold no {ConversionCodes.InstanceNotStripped} error: [{PreviewProblems(preview)}]"
            );
        }
        finally
        {
            File.Delete(ProjectSettings.GlobalizePath(scenePath));
        }
    }

    // A metadata value whose declared path is a plain resource but whose uid names a @tool script is removed as a script: the
    // script neither runs nor loads.
    private static string PreviewStripsAScriptByItsUidsFile()
    {
        const string script = CasesPath + "preview_uid_script.gd";
        string text = UidMappedScene(script, $"  <x:Prop Name=\"metadata/s\" Value='Resource(\"{PreviewPlainPath}\")' />\n");
        string failure = WithoutMarker("gx_preview_uid_script", script, () => WithPreview(text, PreviewPath, scriptsRequested: false, _ => ""));
        return Failures(failure, ResourceLoader.HasCached(script) ? $"{script} is cached after the stripped preview" : "");
    }

    // An ext_resource whose path is a script's uid:// text, typed Resource, is removed as a script, and the value naming it
    // made null. The .xscn text cannot write it (Resource("uid://…") is GXC0111), so the converted document is built.
    private static string PreviewStripsAScriptNamedByUid()
    {
        const string script = CasesPath + "preview_uid_ref.gd";
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "Resource",
                    Path = UidTextOf(script),
                    Id = "1_uid",
                },
            ],
            Nodes =
            [
                new TscnNode
                {
                    Name = "Root",
                    Type = "Control",
                    Properties = [new TscnProperty("metadata/s", new GodotResourceRef(GodotRefKind.External, "1_uid"))],
                },
            ],
        };
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        return Failures(
            stripped.ExtResources.IsEmpty
                ? ""
                : $"the strip kept [{string.Join(", ", stripped.ExtResources.Select(ext => $"{ext.Type} {ext.Path}"))}]",
            stripped.Nodes[0].Properties.FirstOrDefault(property => property.Name == "metadata/s")?.Value is GodotNull
                ? ""
                : "the root's metadata/s is not null after the strip",
            ResourceLoader.HasCached(script) ? $"{script} is cached after the strip" : ""
        );
    }

    // A PackedScene saved as a binary .res and reached through a uid from a metadata value cannot be stripped: no root and a
    // GXC0121 error.
    private static string PreviewRefusesABinaryResourceScene()
    {
        const string scenePath = "user://gxaml-preview-binary.res";
        Error saved = SaveTinyScene(scenePath);
        if (saved != Error.Ok)
        {
            return $"setup: packing and saving {scenePath} gave {saved}";
        }

        // Outside the editor a file saved under user:// gets no uid, so the check registers a fresh one, and takes it back.
        long id = ResourceUid.CreateId();
        ResourceUid.AddId(id, scenePath);
        try
        {
            string text = UidMappedScene(scenePath, $"  <x:Prop Name=\"metadata/s\" Value='Resource(\"{PreviewPlainPath}\")' />\n");
            return RefusedFailure(XscnPreviewScene.Instantiate(text, PreviewPath, scriptsRequested: false));
        }
        finally
        {
            ResourceUid.RemoveId(id);
            File.Delete(ProjectSettings.GlobalizePath(scenePath));
        }
    }

    // Headless, the editor's file types are not known; an instance's ext_resource typed Resource whose uid names a scene is
    // still pointed at a stripped copy in the temp folder, with no uid, and the copy holds no script.
    private static string PreviewRedirectsAResourceTypedInstance()
    {
        string text = UidMappedScene(CasesPath + "preview_uid_inst.xscn", $"  <x:Instance Scene=\"{PreviewPlainPath}\" Name=\"I\" />\n");
        TscnDocument converted = XscnLoading
            .ToTscn(XscnParser.Parse(text, XscnDocumentKind.Scene, XscnMetadata.Value).Document, PreviewPath)
            .Document;
        if (converted.Nodes.Select(node => node.Instance).OfType<GodotResourceRef>().FirstOrDefault() is not { } reference)
        {
            return "setup: the converted text has no instance";
        }

        TscnDocument retyped = converted with
        {
            ExtResources = [.. converted.ExtResources.Select(ext => ext.Id == reference.Id ? ext with { Type = "Resource" } : ext)],
        };
        using var instances = new XscnPreviewInstances();
        TscnExtResource redirected = instances.Stripped(retyped).ExtResources.First(ext => ext.Id == reference.Id);
        return Failures(
            CopyFailure(redirected),
            instances.Issues.Count == 0 ? "" : $"issues: [{string.Join("; ", instances.Issues.Select(issue => issue.Message))}]"
        );
    }

    // Whether the ext_resource points at a copy in the addon's temp folder, with no uid, that holds no script.
    private static string CopyFailure(TscnExtResource redirected)
    {
        string folder = AddonTempFolder.Get().TrimEnd('/') + "/";
        if (!redirected.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(redirected.Path))
        {
            return $"the ext_resource points at {redirected.Path}, not at a copy under {folder}";
        }

        return Failures(
            redirected.Uid is null ? "" : $"the ext_resource keeps uid {redirected.Uid}",
            File.ReadAllText(redirected.Path).Contains("script =", StringComparison.Ordinal) ? "the copy holds a script" : ""
        );
    }

    // Packs a one-node scene and saves it at the path.
    private static Error SaveTinyScene(string path)
    {
        var node = new Control { Name = "Binary" };
        using var packed = new PackedScene();
        Error packError = packed.Pack(node);
        node.Free();
        return packError == Error.Ok ? ResourceSaver.Save(packed, path) : packError;
    }

    // A preview that refused a scene it cannot strip: no root, and a GXC0121 error.
    private static string RefusedFailure(PreviewScene preview)
    {
        preview.Root?.Free();
        bool refused = preview.Issues.Any(issue => issue.Code == ConversionCodes.InstanceNotStripped && issue.Severity == ConversionSeverity.Error);
        return Failures(
            preview.Root is null ? "" : "the preview gave a root",
            refused ? "" : $"the issues hold no {ConversionCodes.InstanceNotStripped} error: [{PreviewProblems(preview)}]"
        );
    }

    // Strips a document whose root's metadata/s names the path through an ext_resource typed Resource, and gives the issues'
    // messages and whether the ext_resource is kept as written.
    private static (string[] Messages, bool Kept) StrippedReference(string path)
    {
        (ConversionIssue[] issues, TscnDocument stripped) = StrippedIssues(ReferencedDocument(path, "Resource"));
        return ([.. issues.Select(issue => issue.Message)], stripped.ExtResources.SingleOrDefault()?.Path == path);
    }

    // The reference's strip gives exactly the one issue message.
    private static string RefusedWith(string path, string expected)
    {
        string[] messages = StrippedReference(path).Messages;
        return messages.SequenceEqual([expected], StringComparer.Ordinal) ? "" : $"the issues are [{string.Join("; ", messages)}], not [{expected}]";
    }

    // A model the editor imports as a scene is refused by its extension, with the message that says to allow scripts.
    private static string PreviewRefusesAnImportedScene()
    {
        const string path = CasesPath + "preview_model.glb";
        return Failures(
            XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.ImportedScene ? "" : $"{path} is not classified ImportedScene",
            RefusedWith(path, $"{path}: an imported scene cannot be previewed with scripts off; allow scripts to preview it")
        );
    }

    // A .res that does not exist is kept, for Godot to report as missing, not refused as unreadable.
    private static string PreviewKeepsAMissingBinaryResource()
    {
        const string path = "user://gxaml-preview-missing.res";
        File.Delete(ProjectSettings.GlobalizePath(path));
        (string[] messages, bool kept) = StrippedReference(path);
        return Failures(
            XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.Kept ? "" : $"{path} is not classified Kept",
            messages.Length == 0 ? "" : $"issues: [{string.Join("; ", messages)}]",
            kept ? "" : "the ext_resource was not kept as written"
        );
    }

    // A .res whose header cannot be read (compressed, truncated, no RSRC magic) fails closed.
    private static string PreviewRefusesAnUnreadableHeader(string name, byte[] bytes)
    {
        string path = "user://gxaml-preview-" + name;
        File.WriteAllBytes(ProjectSettings.GlobalizePath(path), bytes);
        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.UnknownBinary ? "" : $"{path} is not classified UnknownBinary",
                RefusedWith(path, $"{path}: the binary resource's class cannot be read, so it may hold a scene")
            );
        }
        finally
        {
            File.Delete(ProjectSettings.GlobalizePath(path));
        }
    }

    // An upper-case .RES holding a PackedScene is a binary scene: Godot picks loaders by extension in any letter case.
    private static string PreviewRefusesAnUpperCaseBinaryScene()
    {
        const string source = "user://gxaml-preview-upper-source.res";
        const string path = "user://gxaml-preview-upper.RES";
        Error saved = SaveTinyScene(source);
        if (saved != Error.Ok)
        {
            return $"setup: saving {source} gave {saved}";
        }

        File.Copy(ProjectSettings.GlobalizePath(source), ProjectSettings.GlobalizePath(path), overwrite: true);
        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.BinaryScene ? "" : $"{path} is not classified BinaryScene",
                RefusedWith(path, $"{path}: a binary scene cannot be previewed with scripts off")
            );
        }
        finally
        {
            File.Delete(ProjectSettings.GlobalizePath(source));
            File.Delete(ProjectSettings.GlobalizePath(path));
        }
    }

    // A text whose root's metadata/r names the resource at the path through a Resource() value.
    private static string ReferencingResource(string resourcePath) =>
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + $"  <x:Prop Name=\"metadata/r\" Value='Resource(\"{resourcePath}\")' />\n"
        + "</Control>\n";

    // A .tres's built-in @tool script is stripped from its copy: its static initializer does not run, and the preview has
    // a root.
    private static string PreviewStripsAResourcesBuiltInScript()
    {
        const string resource = CasesPath + "preview_res_builtin.tres";
        return WithoutMarker(
            "gx_preview_res_builtin",
            $"{resource}'s built-in script",
            () => WithPreview(ReferencingResource(resource), PreviewPath, scriptsRequested: false, _ => "")
        );
    }

    // A .tres's script file is removed from its copy: the script neither runs nor loads.
    private static string PreviewStripsAResourcesScriptFile() =>
        PreviewStripsAScriptedResource(CasesPath + "preview_res_script.tres", CasesPath + "preview_res_script.gd", "gx_preview_res_script");

    // An .xres's Script= path is removed from its copy: the script neither runs nor loads.
    private static string PreviewStripsAnXres() =>
        PreviewStripsAScriptedResource(CasesPath + "preview_res_x.xres", CasesPath + "preview_res_x.gd", "gx_preview_res_x");

    private static string PreviewStripsAScriptedResource(string resource, string script, string meta)
    {
        string failure = WithoutMarker(meta, script, () => WithPreview(ReferencingResource(resource), PreviewPath, scriptsRequested: false, _ => ""));
        return Failures(failure, ResourceLoader.HasCached(script) ? $"{script} is cached after the stripped preview" : "");
    }

    // A scene a referenced .tres references is stripped too, and a cycle of .tres files returns from the preview, root or
    // none, with no stack overflow and no hang; the cycle's way back points at the first file's stripped copy, so the
    // script the first file names neither runs nor loads.
    private static string PreviewStripsResourcesAtAnyDepth()
    {
        const string cycleScript = CasesPath + "preview_res_script.gd";
        string deep = WithoutMarker(
            "gx_preview_res_depth",
            $"{CasesPath}preview_res_depth.tscn's built-in script",
            () => WithPreview(ReferencingResource(CasesPath + "preview_res_depth.tres"), PreviewPath, scriptsRequested: false, _ => "")
        );
        int tempPathsBeforeCycle = XscnLoadRoute.TempPaths.Count;
        string cycle = WithoutMarker(
            "gx_preview_res_script",
            $"{CasesPath}preview_res_cycle_a.tres's {cycleScript}",
            () =>
            {
                XscnPreviewScene
                    .Instantiate(ReferencingResource(CasesPath + "preview_res_cycle_a.tres"), PreviewPath, scriptsRequested: false)
                    .Root?.Free();
                return "";
            }
        );
        // A failed load keeps its temp file for inspection; the cycle's is expected.
        DeleteKeptTempFiles(tempPathsBeforeCycle);
        return Failures(deep, cycle, ResourceLoader.HasCached(cycleScript) ? $"{cycleScript} is cached after the cycle's stripped preview" : "");
    }

    // An instanced .tscn whose container type is a Resource() path to a script loads with the container kept, and the
    // script is not loaded.
    private static string PreviewRetypesAPathTypedContainer(string scene, string meta, Variant.Type type)
    {
        string failure = WithPreview(
            InstancingScene(CasesPath + scene),
            PreviewPath,
            scriptsRequested: false,
            root =>
            {
                if (root.GetNodeOrNull("Inst") is not { } inst)
                {
                    return $"{scene}: the root has no node Inst";
                }

                using Variant value = inst.GetMeta(meta, default(Variant));
                return value.VariantType == type ? "" : $"{scene}: metadata/{meta} is a {value.VariantType}, not a {type}";
            }
        );
        return Failures(
            failure,
            ResourceLoader.HasCached(PreviewTypedScriptPath) ? $"{scene}: {PreviewTypedScriptPath} is cached after the stripped preview" : ""
        );
    }

    // A Resource() value naming a script in an instanced .tscn, plain or inside an Object() value, is made null: the
    // script neither runs nor loads.
    private static string PreviewNullsAPathReferenceToAScript() =>
        PreviewRunsNoPathValueScript("preview_path_value.tscn", "preview_path_object.tscn");

    // Each instanced case scene, which references preview_path_value.gd, previews with its Inst node, and the script
    // neither runs nor loads.
    private static string PreviewRunsNoPathValueScript(params string[] scenes)
    {
        const string script = CasesPath + "preview_path_value.gd";
        return Failures([
            .. scenes.Select(scene =>
                Failures(
                    WithoutMarker(
                        "gx_preview_path_value",
                        $"{scene}'s {script}",
                        () => WithPreview(InstancingScene(CasesPath + scene), PreviewPath, scriptsRequested: false, root => HasNode(root, "Inst"))
                    ),
                    ResourceLoader.HasCached(script) ? $"{scene}: {script} is cached after the stripped preview" : ""
                )
            ),
        ]);
    }

    // A binary resource that is not a scene cannot be stripped: it is refused with the message that says how to preview it.
    private static string PreviewRefusesABinaryResource()
    {
        const string path = "user://gxaml-preview-binary-resource.res";
        using var box = new StyleBoxFlat();
        Error saved = ResourceSaver.Save(box, path);
        if (saved != Error.Ok)
        {
            return $"setup: saving {path} gave {saved}";
        }

        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.BinaryResource ? "" : $"{path} is not classified BinaryResource",
                RefusedWith(
                    path,
                    $"{path} is a binary resource, which the preview cannot strip of scripts; save it as .tres or turn on "
                        + "godot_xaml/preview_server/allow_scripts"
                )
            );
        }
        finally
        {
            File.Delete(ProjectSettings.GlobalizePath(path));
        }
    }

    // A Resource() value naming a text scene in an instanced .tscn is loaded by its path, never a copy, so it is refused.
    private static string PreviewRefusesAPathValueToAScene()
    {
        const string scene = CasesPath + "preview_inst_script.tscn";
        string expected = $"{scene} is referenced by a Resource() value, which the preview cannot redirect to a stripped copy";
        PreviewScene preview = XscnPreviewScene.Instantiate(
            InstancingScene(CasesPath + "preview_path_scene.tscn"),
            PreviewPath,
            scriptsRequested: false
        );
        return Failures(
            RefusedFailure(preview),
            preview.Issues.Any(issue => issue.Message == expected) ? "" : $"the issues hold no [{expected}]: [{PreviewProblems(preview)}]"
        );
    }

    // A node whose instance= is a Resource() path to a text scene is refused like any path value, since the text loader
    // loads the path as it parses the tag: the scene's script neither runs nor loads.
    private static string PreviewRefusesAPathInstance()
    {
        const string scene = CasesPath + "preview_inst_script.tscn";
        string expected = $"{scene} is referenced by a Resource() value, which the preview cannot redirect to a stripped copy";
        string failure = WithoutMarker(
            PreviewInstancedFileRanMeta,
            PreviewInstancedScriptPath,
            () =>
            {
                PreviewScene preview = XscnPreviewScene.Instantiate(
                    InstancingScene(CasesPath + "preview_path_instance.tscn"),
                    PreviewPath,
                    scriptsRequested: false
                );
                return Failures(
                    RefusedFailure(preview),
                    preview.Issues.Any(issue => issue.Message == expected) ? "" : $"the issues hold no [{expected}]: [{PreviewProblems(preview)}]"
                );
            }
        );
        return Failures(
            failure,
            ResourceLoader.HasCached(PreviewInstancedScriptPath) ? $"{PreviewInstancedScriptPath} is cached after the stripped preview" : ""
        );
    }

    // A tag field the model does not hold, on a node or in the header, is dropped from the stripped copy: the text loader
    // parses it as a value, so its Resource() path would load the script.
    private static string PreviewDropsUnknownTagFields() => PreviewRunsNoPathValueScript("preview_tag_node.tscn", "preview_tag_header.tscn");

    // A referenced .tres that is itself a GDScript is a script: no copy is written, its static initializer does not run,
    // and the reference is null.
    private static string PreviewNullsAResourceThatIsAScript()
    {
        const string resource = CasesPath + "preview_res_is_script.tres";
        return WithoutMarker(
            "gx_preview_res_is_script",
            $"{resource}'s source",
            () =>
                WithPreview(
                    ReferencingResource(resource),
                    PreviewPath,
                    scriptsRequested: false,
                    root =>
                    {
                        using Variant value = root.GetMeta("r", default(Variant));
                        return value.VariantType == Variant.Type.Nil ? "" : $"metadata/r is a {value.VariantType}, not null";
                    }
                )
        );
    }

    // The markers: the static initializers and the _init of the scripts the text's Object() values build.
    private const string PreviewObjectBareMeta = "gx_preview_object_bare";
    private const string PreviewObjectStaticMeta = "gx_preview_object_static";
    private const string PreviewObjectInitMeta = "gx_preview_object_init";

    // A @tool GDScript source, as escaped value text, whose static initializer sets one meta and whose _init another.
    private static string PreviewObjectSource(string extends, string staticMeta, string initMeta) =>
        $"@tool\\nextends {extends}\\n\\nstatic func _static_init():\\n\\tEngine.set_meta(\\\"{staticMeta}\\\", true)\\n\\n"
        + $"func _init():\\n\\tEngine.set_meta(\\\"{initMeta}\\\", true)\\n";

    // The bare GDScript's source, and the source of the one that scripts a Resource.
    private static readonly string PreviewObjectBareSource = PreviewObjectSource("RefCounted", PreviewObjectBareMeta, PreviewObjectBareMeta);
    private static readonly string PreviewObjectScriptedSource = PreviewObjectSource("Resource", PreviewObjectStaticMeta, PreviewObjectInitMeta);

    // "script/source" makes Godot compile the GDScript and run its static initializer as the text loads
    // (gdscript.cpp, GDScript::_set and reload), and "script" then gives the Resource an instance (object.cpp, set_script).
    private static readonly string PreviewObjectScriptScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + $"  <x:Prop Name=\"metadata/g\" Value='Object(GDScript, \"script/source\": \"{PreviewObjectBareSource}\")' />\n"
        + "  <x:Prop Name=\"metadata/s\" Value='Object(Resource, \"script\": "
        + $"Object(GDScript, \"script/source\": \"{PreviewObjectScriptedSource}\"))' />\n"
        + "</Control>\n";

    private static readonly string[] PreviewObjectMetas = [PreviewObjectBareMeta, PreviewObjectStaticMeta, PreviewObjectInitMeta];

    private static void ClearPreviewObjectMetas()
    {
        foreach (string meta in PreviewObjectMetas)
        {
            Engine.Singleton.RemoveMeta(meta);
        }
    }

    // A GDScript built by an Object() value runs no code in a stripped preview, the Resource it scripted staying unscripted;
    // with scripts allowed it runs.
    private static string PreviewObjectValueScriptDoesNotRun()
    {
        using var previous = new PreviewSettingScope();
        Godot.GodotObject engine = Engine.Singleton;
        try
        {
            ClearPreviewObjectMetas();
            ProjectSettings.SetSetting(PreviewScriptsSetting, false);
            string stripped = WithPreview(
                PreviewObjectScriptScene,
                PreviewPath,
                scriptsRequested: true,
                root =>
                {
                    using Variant value = root.GetMeta("s", default(Variant));
                    if (value.AsGodotObject() is not Resource kept)
                    {
                        return "metadata/s is not a Resource";
                    }

                    using Variant script = kept.GetScript();
                    return script.VariantType == Variant.Type.Nil ? "" : "metadata/s is a scripted Resource";
                }
            );
            string[] ranStripped = [.. PreviewObjectMetas.Where(meta => engine.HasMeta(meta))];
            ClearPreviewObjectMetas();
            ProjectSettings.SetSetting(PreviewScriptsSetting, true);
            string allowed = WithPreview(PreviewObjectScriptScene, PreviewPath, scriptsRequested: true, _ => "");
            bool ranAllowed = PreviewObjectMetas.All(meta => engine.HasMeta(meta));
            return Failures(
                stripped,
                ranStripped.Length > 0 ? $"an Object() value's script ran in a stripped preview: {string.Join(", ", ranStripped)}" : "",
                allowed,
                ranAllowed ? "" : "an Object() value's script did not run with scripts allowed"
            );
        }
        finally
        {
            ClearPreviewObjectMetas();
        }
    }

    // A Resource(uid, path) value is classified by both files, since the text loader loads the path when the uid's file
    // does not load: with the uid unregistered, and with it registered to a missing file, the script the path names is
    // made null.
    private static string PreviewClassifiesAPathValuesUidAndPath()
    {
        const string script = CasesPath + "preview_path_value.gd";
        const string scene = "user://gxaml-preview-uid-fallback.tscn";
        long unregistered = ResourceUid.CreateId();
        long missing = ResourceUid.CreateId();
        ResourceUid.AddId(missing, CasesPath + "preview_missing_uid_target.json");
        try
        {
            return Failures([
                .. new[] { unregistered, missing }.Select(id =>
                {
                    File.WriteAllText(
                        ProjectSettings.GlobalizePath(scene),
                        "[gd_scene format=3]\n\n[node name=\"Inst\" type=\"Control\"]\n"
                            + $"metadata/s = Resource(\"{ResourceUid.IdToText(id)}\", \"{script}\")\n"
                    );
                    string which = id == missing ? "a uid registered to a missing file" : "an unregistered uid";
                    return Failures(
                        WithoutMarker(
                            "gx_preview_path_value",
                            $"{which}'s {script}",
                            () => WithPreview(InstancingScene(scene), PreviewPath, scriptsRequested: false, root => HasNode(root, "Inst"))
                        ),
                        ResourceLoader.HasCached(script) ? $"{which}: {script} is cached after the stripped preview" : ""
                    );
                }),
            ]);
        }
        finally
        {
            ResourceUid.RemoveId(missing);
            File.Delete(ProjectSettings.GlobalizePath(scene));
        }
    }

    // A .tres a typed property names is copied as a .tres, which the property's type hint loads by its extension.
    private static string PreviewCopiesAResourceForATypedProperty()
    {
        const string theme = CasesPath + "preview_res_theme.tres";
        string text = $"<Control Name=\"Root\" Theme=\"{theme}\" />\n";
        return WithPreview(
            text,
            PreviewPath,
            scriptsRequested: false,
            root => root is Control { Theme: { DefaultFontSize: 21 } } ? "" : $"the root's theme is not {theme}'s copy"
        );
    }

    // A Resource() value naming a file that is neither a script, a scene nor a resource to strip is kept as written.
    private static string PreviewKeepsAPathValueToAnInertFile() =>
        WithPreview(
            InstancingScene(CasesPath + "preview_path_inert.tscn"),
            PreviewPath,
            scriptsRequested: false,
            root =>
            {
                if (root.GetNodeOrNull("Inst") is not { } inst)
                {
                    return "the root has no node Inst";
                }

                using Variant value = inst.GetMeta("k", default(Variant));
                return value.AsGodotObject() is Shader ? "" : $"metadata/k is a {value.VariantType}, not the kept Shader";
            }
        );

    // A Resource() value naming a script by its uid alone is made null: the script neither runs nor loads.
    private static string PreviewNullsAPathValueByItsUid() => PreviewRunsNoPathValueScript("preview_path_uid.tscn");

    private const string AllowScriptsHint = "turn on godot_xaml/preview_server/allow_scripts to preview it";

    private static string Globalized(string path) => ProjectSettings.GlobalizePath(path);

    private static void DeleteFiles(params string[] paths)
    {
        foreach (string path in paths)
        {
            File.Delete(Globalized(path));
        }
    }

    // A binary scene saved as .theme and as .material is refused as a binary scene: the binary loader claims every resource
    // base extension (resource_format_binary.cpp:1216-1225), and the preview reads the class from the header.
    private static string PreviewRefusesABinarySceneBehindABaseExtension() =>
        PreviewRefusesBehindBaseExtensions(
            "scene",
            SaveTinyScene,
            PreviewReferenceKind.BinaryScene,
            path => $"{path}: a binary scene cannot be previewed with scripts off"
        );

    // A binary resource that is not a scene, saved as .theme and as .material, is refused as a binary resource.
    private static string PreviewRefusesABinaryResourceBehindABaseExtension() =>
        PreviewRefusesBehindBaseExtensions(
            "resource",
            path =>
            {
                using var box = new StyleBoxFlat();
                return ResourceSaver.Save(box, path);
            },
            PreviewReferenceKind.BinaryResource,
            path =>
                $"{path} is a binary resource, which the preview cannot strip of scripts; save it as .tres or turn on "
                + "godot_xaml/preview_server/allow_scripts"
        );

    // Two resource base extensions other than .res, which the binary loader claims.
    private static readonly string[] BaseExtensions = ["theme", "material"];

    // Saves a binary file as .res, copies it as .theme and as .material, and expects each copy classified and refused.
    private static string PreviewRefusesBehindBaseExtensions(
        string what,
        Func<string, Error> save,
        PreviewReferenceKind kind,
        Func<string, string> message
    )
    {
        string source = $"user://gxaml-preview-base-{what}.res";
        Error saved = save(source);
        if (saved != Error.Ok)
        {
            return $"setup: saving {source} gave {saved}";
        }

        try
        {
            return Failures([
                .. BaseExtensions.Select(extension =>
                {
                    string path = $"user://gxaml-preview-base-{what}.{extension}";
                    File.Copy(Globalized(source), Globalized(path), overwrite: true);
                    try
                    {
                        return Failures(
                            XscnPreviewReferences.ClassifyFile(path) == kind ? "" : $"{path} is not classified {kind}",
                            RefusedWith(path, message(path))
                        );
                    }
                    finally
                    {
                        DeleteFiles(path);
                    }
                }),
            ]);
        }
        finally
        {
            DeleteFiles(source);
        }
    }

    // A .tres whose body is a [gd_scene is a text scene, whatever its name: its copy is stripped of the built-in script,
    // whose static initializer does not run, and the reference loads the stripped scene.
    private static string PreviewStripsATextSceneInATres()
    {
        const string path = "user://gxaml-preview-scene-body.tres";
        const string meta = "gx_preview_tres_scene";
        string[] lines =
        [
            "[gd_scene format=3]",
            "",
            "[sub_resource type=\"GDScript\" id=\"GDScript_m\"]",
            "script/source = \"@tool",
            "extends Control",
            "",
            "",
            "static func _static_init():",
            $"\tEngine.set_meta(\\\"{meta}\\\", true)",
            "\"",
            "",
            "[node name=\"Scene\" type=\"Control\"]",
            "script = SubResource(\"GDScript_m\")",
            "",
        ];
        File.WriteAllText(Globalized(path), string.Join("\n", lines));
        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.TextScene ? "" : $"{path} is not classified TextScene",
                WithoutMarker(
                    meta,
                    $"{path}'s built-in script",
                    () =>
                        WithPreview(
                            ReferencingResource(path),
                            PreviewPath,
                            scriptsRequested: false,
                            root =>
                            {
                                using Variant value = root.GetMeta("r", default(Variant));
                                return value.AsGodotObject() is PackedScene ? "" : $"metadata/r is a {value.VariantType}, not the stripped scene";
                            }
                        )
                )
            );
        }
        finally
        {
            DeleteFiles(path);
        }
    }

    // The .import sidecar decides what a file is, read by a text scan that loads nothing: type="PackedScene" beside a file
    // with an unknown extension is an imported scene; a sidecar holding a Resource() value, which Godot's own read of it
    // would load (resource_importer.cpp:44-156, variant_parser.cpp), is refused and nothing loads; type="Texture2D" beside
    // a missing source is kept.
    private static string PreviewClassifiesByImportSidecar()
    {
        const string scene = "user://gxaml-preview-imported.model";
        const string valued = "user://gxaml-preview-import-value.png";
        const string missing = "user://gxaml-preview-import-missing.png";
        const string script = CasesPath + "preview_path_value.gd";
        File.WriteAllText(Globalized(scene), "not a model\n");
        File.WriteAllText(
            Globalized(scene + ".import"),
            "[remap]\n\nimporter=\"scene\"\ntype=\"PackedScene\"\npath=\"user://gxaml-preview-imported.scn\"\n"
        );
        File.WriteAllText(Globalized(valued), "not an image\n");
        File.WriteAllText(Globalized(valued + ".import"), $"[remap]\n\nimporter=\"texture\"\ntype=\"Texture2D\"\nmetadata=Resource(\"{script}\")\n");
        DeleteFiles(missing);
        File.WriteAllText(
            Globalized(missing + ".import"),
            "[remap]\n\nimporter=\"texture\"\ntype=\"Texture2D\"\npath=\"user://gxaml-preview-import-missing.ctex\"\n"
        );
        try
        {
            string valueFailure = WithoutMarker(
                "gx_preview_path_value",
                $"{valued}.import's {script}",
                () =>
                    Failures(
                        XscnPreviewReferences.ClassifyFile(valued) == PreviewReferenceKind.UnsafeSidecar
                            ? ""
                            : $"{valued} is not classified UnsafeSidecar",
                        RefusedWith(
                            valued,
                            $"{valued}'s .import sidecar holds a Resource() or Object() value, which Godot loads as it reads the sidecar; "
                                + AllowScriptsHint
                        )
                    )
            );
            (string[] messages, bool kept) = StrippedReference(missing);
            return Failures(
                XscnPreviewReferences.ClassifyFile(scene) == PreviewReferenceKind.ImportedScene ? "" : $"{scene} is not classified ImportedScene",
                RefusedWith(scene, $"{scene}: an imported scene cannot be previewed with scripts off; allow scripts to preview it"),
                valueFailure,
                ResourceLoader.HasCached(script) ? $"{script} is cached after classifying {valued}" : "",
                XscnPreviewReferences.ClassifyFile(missing) == PreviewReferenceKind.Kept ? "" : $"{missing} is not classified Kept",
                messages.Length == 0 ? "" : $"{missing}: issues: [{string.Join("; ", messages)}]",
                kept ? "" : $"{missing}: the ext_resource was not kept as written"
            );
        }
        finally
        {
            DeleteFiles(scene, scene + ".import", valued, valued + ".import", missing + ".import");
        }
    }

    // The message for a reference whose remap target is a script.
    private static string RemappedToScript(string path, string target) =>
        $"{path} is remapped to {target}, which could carry a script: {target} is a script; {AllowScriptsHint}";

    private static void WriteRemap(string path, string target) => File.WriteAllText(Globalized(path + ".remap"), $"[remap]\n\npath=\"{target}\"\n");

    // A remap is followed, not refused (resource_loader.cpp:1442-1528): a .remap sidecar naming a script .tres is refused,
    // naming the target, beside an existing file and beside a missing one; one naming an inert file previews; a
    // translation_remaps entry with a script target is refused, and one whose targets are all inert previews.
    private static string PreviewFollowsRemaps()
    {
        const string script = CasesPath + "preview_res_is_script.tres";
        const string sided = "user://gxaml-preview-remapped.tres";
        const string missing = "user://gxaml-preview-remapped-missing.tres";
        const string translated = "user://gxaml-preview-translated.tres";
        File.Copy(Globalized(PreviewPlainPath), Globalized(sided), overwrite: true);
        File.Copy(Globalized(PreviewPlainPath), Globalized(translated), overwrite: true);
        DeleteFiles(missing);
        try
        {
            WriteRemap(sided, script);
            string toScript = RefusedWith(sided, RemappedToScript(sided, script));
            WriteRemap(sided, PreviewPlainPath);
            string toInert = WithPreview(ReferencingResource(sided), PreviewPath, scriptsRequested: false, _ => "");
            WriteRemap(missing, script);
            string fromMissing = RefusedWith(missing, RemappedToScript(missing, script));
            return Failures(
                toScript.Length == 0 ? "" : $"a .remap to a script: {toScript}",
                toInert.Length == 0 ? "" : $"a .remap to an inert file: {toInert}",
                fromMissing.Length == 0 ? "" : $"a .remap beside a missing file: {fromMissing}",
                PreviewFollowsTranslationRemaps(translated, script)
            );
        }
        finally
        {
            DeleteFiles(sided, sided + ".remap", missing + ".remap", translated);
        }
    }

    private static string PreviewFollowsTranslationRemaps(string path, string script)
    {
        const string setting = "internationalization/locale/translation_remaps";
        using var previous = new PreviewSettingScope(setting);
        using var scripted = new Godot.Collections.Dictionary { [path] = new[] { $"{script}:de", $"{PreviewPlainPath}:fr" } };
        ProjectSettings.SetSetting(setting, scripted);
        string refused = RefusedWith(path, RemappedToScript(path, script));
        using var inert = new Godot.Collections.Dictionary { [path] = new[] { $"{PreviewPlainPath}:de", $"{PreviewPlainPath}:fr" } };
        ProjectSettings.SetSetting(setting, inert);
        string kept = WithPreview(ReferencingResource(path), PreviewPath, scriptsRequested: false, _ => "");
        return Failures(
            refused.Length == 0 ? "" : $"a translation remap to a script: {refused}",
            kept.Length == 0 ? "" : $"translation remaps to inert files: {kept}"
        );
    }

    // A .gdextension file is refused, since loading one opens a native library (gdextension_resource_format.cpp:36-53);
    // no extension is loaded afterwards.
    private static string PreviewRefusesAGDExtensionLibrary()
    {
        const string path = "user://gxaml-preview-library.gdextension";
        File.WriteAllText(
            Globalized(path),
            "[configuration]\n\nentry_symbol = \"gxaml_preview_init\"\ncompatibility_minimum = \"4.1\"\n\n"
                + "[libraries]\n\nwindows.x86_64 = \"user://gxaml-preview-missing.dll\"\n"
        );
        try
        {
            string expected = $"{path} is a GDExtension library, which loading opens as native code; {AllowScriptsHint}";
            PreviewScene preview = XscnPreviewScene.Instantiate(ReferencingResource(path), PreviewPath, scriptsRequested: false);
            bool named = preview.Issues.Any(issue => issue.Message == expected);
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.NativeLibrary ? "" : $"{path} is not classified NativeLibrary",
                named ? "" : $"the issues hold no [{expected}]: [{PreviewProblems(preview)}]",
                RefusedFailure(preview),
                GDExtensionManager.IsExtensionLoaded(path) ? $"{path} is loaded after the stripped preview" : ""
            );
        }
        finally
        {
            DeleteFiles(path);
        }
    }

    // Strips a document whose root's metadata/s names the path through an ext_resource of the type, and gives the issues'
    // messages and the stripped document.
    private static (string[] Messages, TscnDocument Stripped) StrippedTypedReference(string path, string type)
    {
        (ConversionIssue[] issues, TscnDocument stripped) = StrippedIssues(ReferencedDocument(path, type));
        return ([.. issues.Select(issue => issue.Message)], stripped);
    }

    // "Is it a Script?" asks ClassDB, here through the seam, before the bundled metadata: an ext declared as a class only
    // the seam knows, as a Script, is removed as a script; one declared as a class neither knows is refused.
    private static string PreviewAsksClassDBBeforeTheMetadata()
    {
        const string probed = "GxPreviewProbedScript";
        const string unknown = "GxPreviewUnknownClass";
        XscnPreviewReferences.ClassProbe = (type, baseClass) =>
            type == probed ? baseClass is "Script" or "Resource" or "RefCounted" or "Object" : null;
        try
        {
            (string[] messages, TscnDocument stripped) = StrippedTypedReference(PreviewPlainPath, probed);
            TscnProperty? meta = stripped.Nodes.Single().Properties.FirstOrDefault(property => property.Name == "metadata/s");
            string[] refused = StrippedTypedReference(PreviewPlainPath, unknown).Messages;
            string expected =
                $"{PreviewPlainPath} is declared as {unknown}, a class neither Godot nor the addon's metadata knows, so it may be a script; "
                + AllowScriptsHint;
            return Failures(
                messages.Length == 0 ? "" : $"{probed}: issues: [{string.Join("; ", messages)}]",
                stripped.ExtResources.Length == 0 ? "" : $"the ext_resource declared {probed} was not removed as a script",
                meta?.Value is GodotNull ? "" : $"metadata/s is {meta?.Value}, not null",
                refused.SequenceEqual([expected], StringComparer.Ordinal)
                    ? ""
                    : $"{unknown}: the issues are [{string.Join("; ", refused)}], not [{expected}]"
            );
        }
        finally
        {
            XscnPreviewReferences.ClassProbe = null;
        }
    }

    // A file whose extension none of Godot's own loaders claims is refused, since a project loader script may claim it; a
    // .gdshader, which the shader loader claims, stays kept.
    private static string PreviewRefusesAnUnknownExtension()
    {
        const string path = "user://gxaml-preview-unclaimed.custom";
        const string shader = CasesPath + "preview_path_inert.gdshader";
        File.WriteAllText(Globalized(path), "claimed by no Godot loader\n");
        DeleteFiles(path + ".import");
        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(path) == PreviewReferenceKind.UnknownExtension ? "" : $"{path} is not classified UnknownExtension",
                RefusedWith(
                    path,
                    $"{path} has an extension no Godot loader claims, so a project loader script could run on load; " + AllowScriptsHint
                ),
                XscnPreviewReferences.ClassifyFile(shader) == PreviewReferenceKind.Kept ? "" : $"{shader} is not classified Kept",
                PreviewKeepsAPathValueToAnInertFile()
            );
        }
        finally
        {
            DeleteFiles(path);
        }
    }

    // A GDScript saved by the binary saver and copied to a resource base extension is a script: the binary loader reads the
    // class from the header (resource_format_binary.cpp:1056-1095), whatever the extension.
    private static string PreviewClassifiesABinaryScriptBehindABaseExtension()
    {
        const string source = "user://gxaml-preview-base-script.res";
        const string path = "user://gxaml-preview-base-script.theme";
        using var script = new GDScript { SourceCode = "extends Node\n" };
        Error saved = ResourceSaver.Save(script, source);
        if (saved != Error.Ok)
        {
            return $"setup: saving a GDScript to {source} gave {saved}";
        }

        try
        {
            File.Copy(Globalized(source), Globalized(path), overwrite: true);
            PreviewReferenceKind kind = XscnPreviewReferences.ClassifyFile(path);
            return kind == PreviewReferenceKind.Script ? "" : $"{path} is classified {kind}, not Script";
        }
        finally
        {
            DeleteFiles(source, path);
        }
    }

    // The importer's type and each file it loads decide (resource_importer.cpp:44-201): type="GDScript" is a script,
    // type="GDExtension" a native library, a path= naming a text .tres is refused, a missing path= whose ".editor." variant
    // is a text scene is an imported scene, and a type that is not a plain string is refused.
    private static string PreviewClassifiesByImportedTypeAndTarget()
    {
        const string source = "user://gxaml-preview-import-kind.model";
        const string missing = "user://gxaml-preview-import-kind.ctex";
        const string editor = "user://gxaml-preview-import-kind.editor.ctex";
        File.WriteAllText(Globalized(source), "not a model\n");
        DeleteFiles(missing);
        File.WriteAllText(Globalized(editor), "[gd_scene format=3]\n\n[node name=\"Scene\" type=\"Control\"]\n");
        string Import(string body)
        {
            File.WriteAllText(Globalized(source + ".import"), "[remap]\n\nimporter=\"gxaml\"\n" + body);
            return source;
        }

        try
        {
            PreviewReferenceKind script = XscnPreviewReferences.ClassifyFile(Import("type=\"GDScript\"\n"));
            PreviewReferenceKind library = XscnPreviewReferences.ClassifyFile(Import("type=\"GDExtension\"\n"));
            string libraryRefused = RefusedWith(source, $"{source} is a GDExtension library, which loading opens as native code; {AllowScriptsHint}");
            string textTarget = RefusedWith(
                Import($"type=\"Texture2D\"\npath=\"{PreviewPlainPath}\"\n"),
                $"{source} is imported to {PreviewPlainPath}, a text resource the preview cannot strip in place; {AllowScriptsHint}"
            );
            string editorVariant = RefusedWith(
                Import($"type=\"Texture2D\"\npath=\"{missing}\"\n"),
                $"{source}: an imported scene cannot be previewed with scripts off; allow scripts to preview it"
            );
            string notAString = RefusedWith(
                Import("type=123\n"),
                $"{source}'s .import sidecar has a type that is not a plain string; {AllowScriptsHint}"
            );
            return Failures(
                script == PreviewReferenceKind.Script ? "" : $"type=\"GDScript\": {source} is classified {script}, not Script",
                library == PreviewReferenceKind.NativeLibrary ? "" : $"type=\"GDExtension\": {source} is classified {library}, not NativeLibrary",
                libraryRefused.Length == 0 ? "" : $"type=\"GDExtension\": {libraryRefused}",
                textTarget.Length == 0 ? "" : $"a text .tres target: {textTarget}",
                editorVariant.Length == 0 ? "" : $"an .editor. variant: {editorVariant}",
                notAString.Length == 0 ? "" : $"type=123: {notAString}"
            );
        }
        finally
        {
            DeleteFiles(source, source + ".import", editor);
        }
    }

    // A .json, which Godot's JSON loader keeps, remapped to a text .tres is refused, since the .tres loads unstripped in its
    // place; a .remap whose path is a Resource() value is refused and loads nothing.
    private static string PreviewRefusesUnsafeRemapsOfOtherFiles()
    {
        const string json = "user://gxaml-preview-remapped.json";
        const string sided = "user://gxaml-preview-remap-value.tres";
        const string script = CasesPath + "preview_path_value.gd";
        File.WriteAllText(Globalized(json), "{}\n");
        WriteRemap(json, PreviewPlainPath);
        File.Copy(Globalized(PreviewPlainPath), Globalized(sided), overwrite: true);
        File.WriteAllText(Globalized(sided + ".remap"), $"[remap]\n\npath=Resource(\"{script}\")\n");
        try
        {
            string toText = RefusedWith(
                json,
                $"{json} is remapped to {PreviewPlainPath}, which could carry a script: {PreviewPlainPath} is a text file the preview "
                    + $"cannot strip in place; {AllowScriptsHint}"
            );
            string valued = WithoutMarker(
                "gx_preview_path_value",
                $"{sided}.remap's {script}",
                () =>
                    RefusedWith(
                        sided,
                        $"{sided}'s .remap sidecar holds a Resource() or Object() value, which Godot loads as it reads the sidecar; "
                            + AllowScriptsHint
                    )
            );
            return Failures(
                toText.Length == 0 ? "" : $"a .json remapped to a .tres: {toText}",
                valued.Length == 0 ? "" : $"a .remap holding a Resource() path: {valued}",
                ResourceLoader.HasCached(script) ? $"{script} is cached after classifying {sided}" : ""
            );
        }
        finally
        {
            DeleteFiles(json, json + ".remap", sided, sided + ".remap");
        }
    }

    // A file the shader loader keeps, declared GDExtension, is refused as a native library.
    private static string PreviewRefusesADeclaredGDExtension()
    {
        const string shader = CasesPath + "preview_path_inert.gdshader";
        string[] messages = StrippedTypedReference(shader, "GDExtension").Messages;
        string expected = $"{shader} is a GDExtension library, which loading opens as native code; {AllowScriptsHint}";
        return messages.SequenceEqual([expected], StringComparer.Ordinal)
            ? ""
            : $"declared GDExtension: the issues are [{string.Join("; ", messages)}], not [{expected}]";
    }

    // Sidecar keys are read as VariantParser::parse_tag_assign_eof reads them (variant_parser.cpp:1891-1960): a quoted key,
    // whitespace or a newline inside a key, an assignment on the tag's line and a tag name with a space before it all name
    // the path; a .remap's read ends at its first assignment (resource_loader.cpp:1504-1523), so a later path= is not
    // followed; a quoted "type" key is the type; a sidecar that does not tokenize is refused.
    private static string PreviewReadsSidecarKeysAsGodotDoes()
    {
        const string script = CasesPath + "preview_res_is_script.tres";
        const string path = "user://gxaml-preview-keys.tres";
        const string model = "user://gxaml-preview-keys.model";
        File.Copy(Globalized(PreviewPlainPath), Globalized(path), overwrite: true);
        File.WriteAllText(Globalized(model), "not a model\n");
        (string Name, string Text)[] hidden =
        [
            ("a quoted key", $"[remap]\n\n\"path\"=\"{script}\"\n"),
            ("a key with a space inside", $"[remap]\n\npa th=\"{script}\"\n"),
            ("a key split by a newline", $"[remap]\n\npa\nth=\"{script}\"\n"),
            ("an assignment on the tag's line", $"[remap] path=\"{script}\"\n"),
            ("a tag name after a space", $"[ remap]\npath=\"{script}\"\n"),
        ];
        void Remap(string text) => File.WriteAllText(Globalized(path + ".remap"), text);
        try
        {
            string[] refused =
            [
                .. hidden.Select(item =>
                {
                    Remap(item.Text);
                    string failure = RefusedWith(path, RemappedToScript(path, script));
                    return failure.Length == 0 ? "" : $"{item.Name}: {failure}";
                }),
            ];
            Remap($"[remap]\n\nnote=\"x\"\npath=\"{script}\"\n");
            string firstOnly = WithPreview(ReferencingResource(path), PreviewPath, scriptsRequested: false, _ => "");
            Remap($"[remap]\n\npath=\"{script}\n");
            string unterminated = RefusedWith(path, $"{path}'s .remap sidecar cannot be read as Godot reads it; {AllowScriptsHint}");
            File.WriteAllText(Globalized(model + ".import"), "[remap]\n\nimporter=\"scene\"\n\"type\"=\"PackedScene\"\n");
            string quotedType = RefusedWith(model, $"{model}: an imported scene cannot be previewed with scripts off; allow scripts to preview it");
            return Failures([
                .. refused,
                firstOnly.Length == 0 ? "" : $"a path= after the first assignment: {firstOnly}",
                unterminated.Length == 0 ? "" : $"an unterminated string: {unterminated}",
                quotedType.Length == 0 ? "" : $"a quoted type key: {quotedType}",
            ]);
        }
        finally
        {
            DeleteFiles(path, path + ".remap", model, model + ".import");
        }
    }

    // A loading value is found by its identifier token, as VariantParser's tokenizer reads it, which skips whitespace and
    // ';' comments before the '(' (variant_parser.cpp:215-227, :416-417): each such sidecar is refused and loads nothing.
    // A string that only holds "Resource(" is no loading value, so a sidecar with one and an imported binary texture under
    // res://.godot/imported/ is kept.
    private static string PreviewFindsLoadingValuesByToken()
    {
        const string script = CasesPath + "preview_path_value.gd";
        const string spaced = "user://gxaml-preview-token-spaced.png";
        const string commented = "user://gxaml-preview-token-commented.png";
        const string quoted = "user://gxaml-preview-token-quoted.png";
        const string texture = "res://.godot/imported/gxaml-preview-token-quoted.res";
        const string head = "[remap]\n\nimporter=\"texture\"\ntype=\"Texture2D\"\n";
        foreach (string source in new[] { spaced, commented, quoted })
        {
            File.WriteAllText(Globalized(source), "not an image\n");
        }

        File.WriteAllText(Globalized(spaced + ".import"), $"{head}metadata=Resource (\"{script}\")\n");
        File.WriteAllText(Globalized(commented + ".import"), $"{head}metadata=Resource ;c\n(\"{script}\")\n");
        File.WriteAllText(Globalized(quoted + ".import"), $"[remap]\n\nimporter=\"Resource(x\"\ntype=\"Texture2D\"\npath=\"{texture}\"\n");
        Directory.CreateDirectory(Globalized("res://.godot/imported"));
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        using var saved = ImageTexture.CreateFromImage(image);
        Error error = ResourceSaver.Save(saved, texture);
        try
        {
            if (error != Error.Ok)
            {
                return $"setup: saving {texture} gave {error}";
            }

            string[] refused =
            [
                .. new[] { spaced, commented }.Select(source =>
                    WithoutMarker(
                        "gx_preview_path_value",
                        $"{source}.import's {script}",
                        () =>
                            Failures(
                                XscnPreviewReferences.ClassifyFile(source) == PreviewReferenceKind.UnsafeSidecar
                                    ? ""
                                    : $"{source} is not classified UnsafeSidecar",
                                RefusedWith(
                                    source,
                                    $"{source}'s .import sidecar holds a Resource() or Object() value, which Godot loads as it reads the "
                                        + $"sidecar; {AllowScriptsHint}"
                                )
                            )
                    )
                ),
            ];
            (string[] messages, bool kept) = StrippedReference(quoted);
            return Failures([
                .. refused,
                ResourceLoader.HasCached(script) ? $"{script} is cached after classifying the sidecars" : "",
                XscnPreviewReferences.ClassifyFile(quoted) == PreviewReferenceKind.Kept ? "" : $"{quoted} is not classified Kept",
                messages.Length == 0 ? "" : $"{quoted}: issues: [{string.Join("; ", messages)}]",
                kept ? "" : $"{quoted}: the ext_resource was not kept as written",
            ]);
        }
        finally
        {
            DeleteFiles(spaced, spaced + ".import", commented, commented + ".import", quoted, quoted + ".import", texture);
        }
    }

    // valid=false only sets r_valid, which load_internal does not pass (resource_importer.cpp:114-117, :174), so the
    // importer still loads the file: a PackedScene type beside it is an imported scene.
    private static string PreviewLoadsThroughAnInvalidImport()
    {
        const string source = "user://gxaml-preview-invalid.model";
        File.WriteAllText(Globalized(source), "not a model\n");
        File.WriteAllText(Globalized(source + ".import"), "[remap]\n\nvalid=false\nimporter=\"scene\"\ntype=\"PackedScene\"\n");
        try
        {
            return Failures(
                XscnPreviewReferences.ClassifyFile(source) == PreviewReferenceKind.ImportedScene ? "" : $"{source} is not classified ImportedScene",
                RefusedWith(source, $"{source}: an imported scene cannot be previewed with scripts off; allow scripts to preview it")
            );
        }
        finally
        {
            DeleteFiles(source, source + ".import");
        }
    }

    // Each file the importer loads goes through the full classifier: an .xscn is an imported scene; a binary resource is
    // kept only under res://.godot/imported/; an unclaimed extension is refused; a target with an .import of its own,
    // which the importer would hand to the importer again (resource_importer.cpp:191), is refused.
    private static string PreviewClassifiesImportTargetsFully()
    {
        const string source = "user://gxaml-preview-target.png";
        const string scene = "user://gxaml-preview-target.xscn";
        const string binary = "user://gxaml-preview-target.res";
        const string custom = "user://gxaml-preview-target.custom";
        const string imported = "res://.godot/imported/gxaml-preview-target.res";
        const string chained = "user://gxaml-preview-target-chained.png";
        File.WriteAllText(Globalized(source), "not an image\n");
        File.WriteAllText(Globalized(custom), "claimed by no Godot loader\n");
        File.WriteAllText(Globalized(chained + ".import"), "[remap]\n\nimporter=\"texture\"\ntype=\"Texture2D\"\n");
        Directory.CreateDirectory(Globalized("res://.godot/imported"));
        using var box = new StyleBoxFlat();
        Error savedBinary = ResourceSaver.Save(box, binary);
        Error savedImported = ResourceSaver.Save(box, imported);
        string ImportedTo(string target)
        {
            File.WriteAllText(Globalized(source + ".import"), $"[remap]\n\nimporter=\"gxaml\"\ntype=\"Resource\"\npath=\"{target}\"\n");
            return source;
        }

        try
        {
            if (savedBinary != Error.Ok || savedImported != Error.Ok)
            {
                return $"setup: saving {binary} gave {savedBinary}, {imported} gave {savedImported}";
            }

            string toScene = RefusedWith(
                ImportedTo(scene),
                $"{source}: an imported scene cannot be previewed with scripts off; allow scripts to preview it"
            );
            string toBinary = RefusedWith(
                ImportedTo(binary),
                $"{source} is imported to {binary}: {binary} is a binary resource, which the preview cannot strip of scripts; save it "
                    + "as .tres or turn on godot_xaml/preview_server/allow_scripts"
            );
            string toCustom = RefusedWith(
                ImportedTo(custom),
                $"{source} is imported to {custom}: {custom} has an extension no Godot loader claims, so a project loader script could "
                    + $"run on load; {AllowScriptsHint}"
            );
            (string[] messages, bool kept) = StrippedReference(ImportedTo(imported));
            string toChained = RefusedWith(
                ImportedTo(chained),
                $"{source} is imported to {chained}, which has an .import sidecar of its own; {AllowScriptsHint}"
            );
            return Failures(
                Labeled("an .xscn target", toScene),
                Labeled("a binary target outside res://.godot/imported/", toBinary),
                Labeled("a .custom target", toCustom),
                Labeled("a binary target under res://.godot/imported/", KeptFailure(messages, kept)),
                Labeled("a target with an .import", toChained)
            );
        }
        finally
        {
            DeleteFiles(source, source + ".import", binary, custom, imported, chained + ".import");
        }
    }

    // A sidecar Godot reads more of, or decodes otherwise, than a decoded string shows is refused: a NUL byte, which Godot's
    // StreamFile reads past (variant_parser.cpp:74-89, :1933) where a decoded string stops (ustring.cpp:1782), and a byte
    // order mark in a quoted key, a tag name or a path, which Godot's narrowing drops (ustring.cpp:1756-1766). An
    // assignment with no key ends an .import's read (resource_importer.cpp:90, :120-121), so a type= after it is not read.
    private static string PreviewRefusesSidecarTextGodotReadsOtherwise()
    {
        const string script = CasesPath + "preview_res_is_script.tres";
        const string path = "user://gxaml-preview-unreadable.tres";
        const string model = "user://gxaml-preview-unreadable.model";
        File.Copy(Globalized(PreviewPlainPath), Globalized(path), overwrite: true);
        File.WriteAllText(Globalized(model), "not a model\n");
        File.WriteAllText(Globalized(model + ".import"), "[remap]\n\nimporter=\"scene\"\n=\"x\"\ntype=\"PackedScene\"\n");
        (string Name, string Text)[] unreadable =
        [
            ("a NUL line before path=", $"[remap]\n\0\npath=\"{script}\"\n"),
            ("a quoted key after a BOM", $"[remap]\n\n\"\uFEFFpath\"=\"{script}\"\n"),
            ("a tag name after a BOM", $"[\uFEFFremap]\npath=\"{script}\"\n"),
            ("a path after a BOM", $"[remap]\n\npath=\"\uFEFF{script}\"\n"),
        ];
        try
        {
            string[] refused =
            [
                .. unreadable.Select(item =>
                {
                    File.WriteAllText(Globalized(path + ".remap"), item.Text);
                    return Labeled(item.Name, RefusedWith(path, $"{path}'s .remap sidecar cannot be read as Godot reads it; {AllowScriptsHint}"));
                }),
            ];
            string keyless = RefusedWith(
                model,
                $"{model} has an extension no Godot loader claims, so a project loader script could run on load; {AllowScriptsHint}"
            );
            return Failures([.. refused, Labeled("an assignment with no key before type=", keyless)]);
        }
        finally
        {
            DeleteFiles(path, path + ".remap", model, model + ".import");
        }
    }

    // Every loading value is found wherever it sits: a Resource() nested in a dictionary, an array or an Object(), an
    // ExtResource(), a SubResource() or an Object() alone, and a Resource() past the [deps] tag, where Godot's load stops
    // but the editor's reads go on; a NUL before one makes the sidecar unreadable. Each is refused and loads nothing.
    private static string PreviewFindsEveryLoadingValue()
    {
        const string script = CasesPath + "preview_path_value.gd";
        const string source = "user://gxaml-preview-token-every.png";
        const string head = "[remap]\n\nimporter=\"texture\"\ntype=\"Texture2D\"\n";
        const string holds = "sidecar holds a Resource() or Object() value, which Godot loads as it reads the sidecar";
        const string unreadable = "sidecar cannot be read as Godot reads it";
        (string Name, string Body, string Reason)[] cases =
        [
            ("a Resource() in a dictionary", $"metadata={{\"a\": Resource(\"{script}\")}}\n", holds),
            ("a Resource() in an array", $"metadata=[Resource(\"{script}\")]\n", holds),
            ("a Resource() inside Object()", $"metadata=Object(RefCounted, \"script\": Resource(\"{script}\"))\n", holds),
            ("an ExtResource()", "metadata=ExtResource(\"1_a\")\n", holds),
            ("a SubResource()", "metadata=SubResource(\"1_a\")\n", holds),
            ("an Object()", "metadata=Object(RefCounted)\n", holds),
            ("a Resource() after [deps]", $"\n[deps]\n\nsource=Resource(\"{script}\")\n", holds),
            ("a NUL before a Resource()", $"\0\nmetadata=Resource(\"{script}\")\n", unreadable),
        ];
        File.WriteAllText(Globalized(source), "not an image\n");
        try
        {
            string[] refused =
            [
                .. cases.Select(item =>
                {
                    File.WriteAllText(Globalized(source + ".import"), head + item.Body);
                    return Labeled(
                        item.Name,
                        WithoutMarker(
                            "gx_preview_path_value",
                            $"{source}.import's {script}",
                            () => RefusedWith(source, $"{source}'s .import {item.Reason}; {AllowScriptsHint}")
                        )
                    );
                }),
            ];
            return Failures([.. refused, ResourceLoader.HasCached(script) ? $"{script} is cached after classifying the sidecars" : ""]);
        }
        finally
        {
            DeleteFiles(source, source + ".import");
        }
    }

    // A target with a ".." segment walks out of res://.godot/imported/ (FileAccess::fix_path resolves none,
    // file_access.cpp:254-277), so it is refused before it is classified, here naming a project binary resource.
    private static string PreviewRefusesAnImportTargetThatLeavesTheImporterFolder()
    {
        const string source = "user://gxaml-preview-escape.png";
        const string binary = "res://gxaml-preview-escape.res";
        const string target = "res://.godot/imported/../../gxaml-preview-escape.res";
        File.WriteAllText(Globalized(source), "not an image\n");
        File.WriteAllText(Globalized(source + ".import"), $"[remap]\n\nimporter=\"gxaml\"\ntype=\"Resource\"\npath=\"{target}\"\n");
        using var box = new StyleBoxFlat();
        Error saved = ResourceSaver.Save(box, binary);
        try
        {
            return saved != Error.Ok
                ? $"setup: saving {binary} gave {saved}"
                : RefusedWith(source, $"{source} is imported to {target}, a path that leaves Godot's importer folder; {AllowScriptsHint}");
        }
        finally
        {
            DeleteFiles(source, source + ".import", binary);
        }
    }

    // An .import that names no type still loads through the importer when its read reaches the end
    // (resource_importer.cpp:78-84, :191), so a .json beside one whose path= names a scene is refused; when the importer's
    // view and the binary loader's are as restrictive, the importer's is the one reported.
    private static string PreviewClassifiesATypelessOrTiedImport()
    {
        const string json = "user://gxaml-preview-typeless.json";
        const string scene = CasesPath + "preview_inst_script.tscn";
        const string tied = "user://gxaml-preview-tied.res";
        File.WriteAllText(Globalized(json), "{}\n");
        File.WriteAllText(Globalized(json + ".import"), $"path=\"{scene}\"\n");
        Error saved = SaveTinyScene(tied);
        File.WriteAllText(Globalized(tied + ".import"), "[remap]\n\nimporter=\"scene\"\ntype=\"PackedScene\"\n");
        try
        {
            return saved != Error.Ok
                ? $"setup: saving {tied} gave {saved}"
                : Failures(
                    Labeled(
                        "a typeless .import beside a .json",
                        RefusedWith(json, $"{json}: an imported scene cannot be previewed with scripts off; allow scripts to preview it")
                    ),
                    Labeled(
                        "an imported scene beside a binary scene",
                        RefusedWith(tied, $"{tied}: an imported scene cannot be previewed with scripts off; allow scripts to preview it")
                    )
                );
        }
        finally
        {
            DeleteFiles(json, json + ".import", tied, tied + ".import");
        }
    }

    // The failure under a label, or none.
    private static string Labeled(string label, string failure) => failure.Length == 0 ? "" : $"{label}: {failure}";

    // A strip that kept the reference as written: no issues, and the ext_resource unchanged.
    private static string KeptFailure(string[] messages, bool kept) =>
        Failures(messages.Length == 0 ? "" : $"issues: [{string.Join("; ", messages)}]", kept ? "" : "the ext_resource was not kept as written");

    // A front loader recognizes a file only for some type hints, and a failed load goes on to the importer
    // (resource_loader.cpp:62-83, :135-139, :284-293): a .gdshader declared Theme, and a missing .tres, each with an .import
    // of type PackedScene, are refused as imported scenes; a .gdshader with no sidecar stays kept.
    private static string PreviewClassifiesTheImporterBesideAFrontLoader()
    {
        const string shader = "user://gxaml-preview-front.gdshader";
        const string missing = "user://gxaml-preview-front-missing.tres";
        const string inert = CasesPath + "preview_path_inert.gdshader";
        const string sceneImport = "[remap]\n\nimporter=\"scene\"\ntype=\"PackedScene\"\n";
        File.WriteAllText(Globalized(shader), "shader_type canvas_item;\n");
        File.WriteAllText(Globalized(shader + ".import"), sceneImport);
        DeleteFiles(missing);
        File.WriteAllText(Globalized(missing + ".import"), sceneImport);
        try
        {
            string[] messages = StrippedTypedReference(shader, "Theme").Messages;
            string expected = $"{shader}: an imported scene cannot be previewed with scripts off; allow scripts to preview it";
            return Failures(
                messages.SequenceEqual([expected], StringComparer.Ordinal)
                    ? ""
                    : $"a .gdshader declared Theme: the issues are [{string.Join("; ", messages)}], not [{expected}]",
                RefusedWith(missing, $"{missing}: an imported scene cannot be previewed with scripts off; allow scripts to preview it"),
                XscnPreviewReferences.ClassifyFile(inert) == PreviewReferenceKind.Kept ? "" : $"{inert} is not classified Kept"
            );
        }
        finally
        {
            DeleteFiles(shader, shader + ".import", missing + ".import");
        }
    }

    // A missing file whose extension none of Godot's own loaders claims is refused: Godot asks every loader for it with no
    // existence test (resource_loader.cpp:284-293). A missing .json, which the JSON loader claims, stays kept.
    private static string PreviewRefusesAMissingUnknownExtension()
    {
        const string absent = "user://gxaml-preview-absent.custom";
        const string json = "user://gxaml-preview-absent.json";
        DeleteFiles(absent, absent + ".import", absent + ".remap", json, json + ".import", json + ".remap");
        (string[] messages, bool kept) = StrippedReference(json);
        return Failures(
            XscnPreviewReferences.ClassifyFile(absent) == PreviewReferenceKind.UnknownExtension ? "" : $"{absent} is not classified UnknownExtension",
            RefusedWith(
                absent,
                $"{absent} has an extension no Godot loader claims, so a project loader script could run on load; {AllowScriptsHint}"
            ),
            XscnPreviewReferences.ClassifyFile(json) == PreviewReferenceKind.Kept ? "" : $"{json} is not classified Kept",
            messages.Length == 0 ? "" : $"{json}: issues: [{string.Join("; ", messages)}]",
            kept ? "" : $"{json}: the ext_resource was not kept as written"
        );
    }

    // A one-ext_resource scene whose root's metadata/s names the path through an ext_resource of the type.
    private static TscnDocument ReferencedDocument(string path, string type) =>
        new()
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = type,
                    Path = path,
                    Id = "1_ref",
                },
            ],
            Nodes =
            [
                new TscnNode
                {
                    Name = "Root",
                    Type = "Control",
                    Properties = [new TscnProperty("metadata/s", new GodotResourceRef(GodotRefKind.External, "1_ref"))],
                },
            ],
        };

    // The issues a strip of the document leaves, and the document it returns.
    private static (ConversionIssue[] Issues, TscnDocument Stripped) StrippedIssues(TscnDocument document)
    {
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        return ([.. instances.Issues], stripped);
    }

    // With scripts off, a project that declares a global class whose native base is ResourceFormatLoader gets no preview:
    // Godot registers it as a loader, and a built-in loader that fails on a file hands it on to them (resource_loader.cpp,
    // add_custom_loaders). The addon's own two loaders are exempt. The script extensions are a fixed table, so classifying
    // a file runs no project loader's code.
    private static string PreviewRefusesAProjectWithALoaderClass() =>
        Failures(
            PreviewRefusesALoaderClass(),
            PreviewStripsWithoutALoaderClass(),
            PreviewExemptsItsOwnLoaders(),
            PreviewClassifiesScriptExtensionsByTable()
        );

    // A class whose native base is ResourceFormatLoader refuses the whole preview: one issue per class, at the class's
    // script path, naming it. A class extending one, a class whose bases repeat, and a class that merely shares the addon
    // loader's name are refused the same; a class extending Resource is not, and nothing is classified or copied.
    private static string PreviewRefusesALoaderClass()
    {
        (string Class, string Path)[] refused =
        [
            ("GxDirectLoader", "res://loaders/gx_direct.gd"),
            ("GxIndirectLoader", "res://loaders/gx_indirect.gd"),
            ("GxCycleA", "res://loaders/gx_cycle_a.gd"),
            ("GxCycleB", "res://loaders/gx_cycle_b.gd"),
            (nameof(XscnFormatLoader), "res://loaders/not_the_addon.gd"),
        ];
        XscnPreviewReferences.GlobalClassProbe = () =>
            [
                ("GxDirectLoader", "res://loaders/gx_direct.gd", "ResourceFormatLoader"),
                ("GxIndirectLoader", "res://loaders/gx_indirect.gd", "GxDirectLoader"),
                ("GxCycleA", "res://loaders/gx_cycle_a.gd", "GxCycleB"),
                ("GxCycleB", "res://loaders/gx_cycle_b.gd", "GxCycleA"),
                (nameof(XscnFormatLoader), "res://loaders/not_the_addon.gd", "ResourceFormatLoader"),
                ("GxPlainResource", "res://loaders/gx_plain.gd", "Resource"),
            ];
        string folder = AddonTempFolder.Get();
        string[] before = Directory.GetFiles(folder);
        try
        {
            (ConversionIssue[] issues, TscnDocument stripped) = StrippedIssues(ReferencedDocument(PreviewPlainPath, "Resource"));
            string[] written = [.. Directory.GetFiles(folder).Except(before, StringComparer.Ordinal)];
            string[] messages = [.. issues.Select(issue => issue.Message)];
            string[] expected = [.. refused.Select(item => LoaderClassMessage(item.Class))];
            return Failures(
                messages.SequenceEqual(expected, StringComparer.Ordinal)
                    ? ""
                    : $"the issues are [{string.Join("; ", messages)}], not [{string.Join("; ", expected)}]",
                issues.All(issue => issue.Code == ConversionCodes.InstanceNotStripped)
                    ? ""
                    : $"the codes are [{string.Join(", ", issues.Select(issue => issue.Code))}]",
                issues.All(issue => issue.Severity == ConversionSeverity.Error)
                    ? ""
                    : $"the severities are [{string.Join(", ", issues.Select(issue => issue.Severity))}]",
                issues.Select(issue => issue.Location).SequenceEqual(refused.Select(item => item.Path), StringComparer.Ordinal)
                    ? ""
                    : $"the issues are located at [{string.Join(", ", issues.Select(issue => issue.Location))}]",
                stripped.ExtResources.Single().Path == PreviewPlainPath
                    ? ""
                    : $"the document was changed: the ext_resource points at {stripped.ExtResources.Single().Path}",
                written.Length == 0 ? "" : $"the strip wrote copies: [{string.Join(", ", written)}]"
            );
        }
        finally
        {
            XscnPreviewReferences.GlobalClassProbe = null;
        }
    }

    // The refusal's message for a project's loader class, as the strip words it.
    private static string LoaderClassMessage(string className) =>
        $"{className} is a ResourceFormatLoader class, which Godot registers as a loader that a failed load hands files to; " + AllowScriptsHint;

    // Only the addon's own loaders, at their own scripts, and a class extending Resource: nothing is refused, and the
    // reference is copied as the strip does.
    private static string PreviewStripsWithoutALoaderClass()
    {
        XscnPreviewReferences.GlobalClassProbe = () =>
            [
                AddonLoader(nameof(XscnFormatLoader)),
                AddonLoader(nameof(XresFormatLoader)),
                ("GxPlainResource", "res://loaders/gx_plain.gd", "Resource"),
            ];
        try
        {
            (string[] messages, bool kept) = StrippedReference(PreviewPlainPath);
            return Failures(
                messages.Length == 0 ? "" : $"the issues are [{string.Join("; ", messages)}]",
                kept ? "the reference was not copied" : ""
            );
        }
        finally
        {
            XscnPreviewReferences.GlobalClassProbe = null;
        }
    }

    // The addon's own loader class as the class list records it: the class named by its script, at the script's path.
    private static (string Class, string Path, string Base) AddonLoader(string className) =>
        (className, XscnFormatLoader.LoaderScripts.Single(script => Path.GetFileNameWithoutExtension(script) == className), "ResourceFormatLoader");

    // The checks project's real class list holds the addon's loaders, each walking to ResourceFormatLoader, and
    // ProjectLoaderClasses refuses none of them.
    private static string PreviewExemptsItsOwnLoaders()
    {
        (string[] classes, Dictionary<string, string> baseOfClass) = RealGlobalClasses();
        string? nativeBase = NativeBaseIn(baseOfClass, nameof(XscnFormatLoader));
        IReadOnlyList<(string Class, string Path)> loaders = XscnPreviewReferences.ProjectLoaderClasses();
        return Failures(
            classes.Contains(nameof(XscnFormatLoader), StringComparer.Ordinal) ? "" : $"the project's class list holds no {nameof(XscnFormatLoader)}",
            nativeBase == "ResourceFormatLoader"
                ? ""
                : $"{nameof(XscnFormatLoader)}'s walk reaches {nativeBase ?? "no native base"}, not ResourceFormatLoader",
            loaders.Count == 0
                ? ""
                : $"the project's loader classes are [{string.Join(", ", loaders.Select(loader => $"{loader.Class} at {loader.Path}"))}]"
        );
    }

    // Each global class the project's own cache holds, in its order, with its direct base.
    private static (string[] Classes, Dictionary<string, string> BaseOfClass) RealGlobalClasses()
    {
        List<string> classes = [];
        var baseOfClass = new Dictionary<string, string>(StringComparer.Ordinal);
        using Variant classKey = "class";
        using Variant baseKey = "base";
        using var list = (Godot.Collections.Array)ProjectSettings.GetGlobalClassList();
        foreach (Variant item in list)
        {
            using (item)
            {
                using Godot.Collections.Dictionary entry = item.AsGodotDictionary();
                using Variant className = entry[classKey];
                using Variant baseName = entry[baseKey];
                classes.Add(className.AsString());
                baseOfClass[className.AsString()] = baseName.AsString();
            }
        }

        return ([.. classes], baseOfClass);
    }

    // The engine class the project's list walks a class to, as the preview walks it; null when the list repeats a class.
    private static string? NativeBaseIn(Dictionary<string, string> baseOfClass, string className)
    {
        if (!baseOfClass.TryGetValue(className, out string? current))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { className };
        while (baseOfClass.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // The script extensions are the fixed table, whatever the case: GDScript's text and binary savers and C# are scripts, and
    // a .gdshader is not one.
    private static string PreviewClassifiesScriptExtensionsByTable()
    {
        string script = CasesPath + "preview_root.gd";
        string binary = CasesPath + "preview_root.GDC";
        string sharp = CasesPath + "CheckedResource.cs";
        string shader = CasesPath + "preview_path_inert.gdshader";
        return Failures(
            XscnPreviewReferences.ClassifyFile(script) == PreviewReferenceKind.Script ? "" : $"{script} is not classified Script",
            XscnPreviewReferences.ClassifyFile(binary) == PreviewReferenceKind.Script ? "" : $"{binary} is not classified Script",
            XscnPreviewReferences.ClassifyFile(sharp) == PreviewReferenceKind.Script ? "" : $"{sharp} is not classified Script",
            XscnPreviewReferences.ClassifyFile(shader) == PreviewReferenceKind.Script ? $"{shader} is classified Script" : ""
        );
    }

    // With scripts off, a node's instance placeholder names a scene the editor's instantiation loads as a real instance
    // (packed_scene.cpp:241-244, editor_node.cpp) and always with the PackedScene hint, so that scene is stripped and
    // copied and the placeholder points at the copy. The path resolves as Godot resolves it: a uid first, a path with no
    // scheme against res://, anything else localized; an unregistered uid names nothing and stays as written. A placeholder
    // to a script, a text resource, a file that is not a scene or a binary scene is refused in place, since its path cannot
    // be removed. A missing file of a kept kind stays as written, while a missing text scene is refused with the read
    // failure, as a missing instance's is. A connection into the placeholder's child that lost its script is dropped.
    private static string PreviewStripsAnInstancePlaceholdersScene()
    {
        string scene = CasesPath + "preview_inst_script.tscn";
        string relative = scene["res://".Length..];
        // A .tscn gets no uid from the import scan, so the uid case names a fixture the import gives one, which has a .uid.
        string uidScene = CasesPath + "preview_scripted.xscn";
        string uid = UidTextOf(uidScene);
        string climb = "res://checks/../checks/cases/preview_inst_script.tscn";
        const string absentUid = "uid://gxamlplaceholderabsent";
        string child = "user://gxaml-preview-placeholder-child.tscn";
        string gdscript = "user://gxaml-preview-placeholder-gdscript.tscn";
        string json = "user://gxaml-preview-placeholder.json";
        string outer = "user://gxaml-preview-placeholder-outer.tscn";
        const string binary = "user://gxaml-preview-placeholder.scn";
        File.WriteAllText(
            Globalized(child),
            "[gd_scene format=3]\n\n"
                + $"[ext_resource type=\"Script\" path=\"{CasesPath}preview_inst_script.gd\" id=\"1_s\"]\n\n"
                + "[node name=\"Root\" type=\"Control\"]\n\n"
                + "[node name=\"Child\" type=\"Control\" parent=\".\"]\n"
                + "script = ExtResource(\"1_s\")\n"
        );
        File.WriteAllText(Globalized(gdscript), "[gd_resource type=\"GDScript\" format=3]\n\n[resource]\n");
        File.WriteAllText(Globalized(json), "{}\n");
        try
        {
            var node = new Control { Name = "Binary" };
            using var packed = new PackedScene();
            Error packError = packed.Pack(node);
            node.Free();
            Error saved = packError == Error.Ok ? ResourceSaver.Save(packed, binary) : packError;
            return Failures(
                saved == Error.Ok ? "" : $"setup: saving {binary} gave {saved}",
                PlaceholderPathOf(uid, uidScene),
                PlaceholderPathOf(relative, scene),
                PlaceholderPathOf(climb, scene),
                XscnPreviewReferences.PlaceholderPath(absentUid) is null
                    ? ""
                    : $"{absentUid} resolves to {XscnPreviewReferences.PlaceholderPath(absentUid)}, not null",
                PlaceholderStripped(scene),
                PlaceholderStripped(uidScene),
                PlaceholderStripped(uid),
                PlaceholderStripped(relative),
                PlaceholderStripped(climb),
                PlaceholderCopiedOnce(scene, climb),
                PlaceholderNested(outer, scene),
                PlaceholderRefusedWith(
                    CasesPath + "preview_inst_script.gd",
                    $"{CasesPath}preview_inst_script.gd is a script that an instance placeholder would load; {AllowScriptsHint}"
                ),
                PlaceholderRefusedWith(
                    PreviewPlainPath,
                    $"{PreviewPlainPath} is a text resource that an instance placeholder would load as a scene; {AllowScriptsHint}"
                ),
                PlaceholderRefusedWith(gdscript, $"{gdscript} is a script that an instance placeholder would load; {AllowScriptsHint}"),
                PlaceholderRefusedWith(binary, $"{binary}: a binary scene cannot be previewed with scripts off"),
                // The PackedScene hint makes a file that is not a scene a refusal, unless it is a missing file, which Godot
                // loads nothing for.
                PlaceholderRefusedWith(json, $"{json} is not a text scene"),
                PlaceholderKept("user://gxaml-preview-placeholder-absent.json"),
                PlaceholderRefusedWith(
                    "user://gxaml-preview-placeholder-absent.tscn",
                    "user://gxaml-preview-placeholder-absent.tscn: the scene cannot be read: FileNotFound"
                ),
                PlaceholderUnchanged(absentUid),
                PlaceholderConnectionDropped(child)
            );
        }
        finally
        {
            DeleteFiles(child, gdscript, json, outer, binary);
        }
    }

    // The placeholder's path resolves to the expected one.
    private static string PlaceholderPathOf(string placeholder, string expected)
    {
        string? resolved = XscnPreviewReferences.PlaceholderPath(placeholder);
        return resolved == expected ? "" : $"{placeholder} resolves to {resolved}, not {expected}";
    }

    // The strip of a scene whose root's instance placeholder names the path, the path the placeholder now names and the
    // text of the copy, read before the strip deletes its copies.
    private static (ConversionIssue[] Issues, TscnDocument Stripped, string? Copy, string? Text) PlaceholderStrip(string placeholder)
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            Nodes =
            [
                new TscnNode
                {
                    Name = "P",
                    Type = "Control",
                    InstancePlaceholder = placeholder,
                },
            ],
        };
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        string? copy = stripped.Nodes.Single().InstancePlaceholder;
        string folder = AddonTempFolder.Get();
        string? text = copy is { } path && path.StartsWith(folder, StringComparison.Ordinal) ? File.ReadAllText(path) : null;
        return ([.. instances.Issues], stripped, copy, text);
    }

    // The placeholder names the addon's copy of a scripted scene, and the copy holds no script.
    private static string PlaceholderStripped(string placeholder)
    {
        (ConversionIssue[] issues, _, string? copy, string? text) = PlaceholderStrip(placeholder);
        string folder = AddonTempFolder.Get();
        string problems = issues.Length == 0 ? "" : $"the issues are [{string.Join("; ", issues.Select(issue => issue.Message))}]";
        if (copy is not { } path || !path.StartsWith(folder, StringComparison.Ordinal))
        {
            return Failures(problems, $"the placeholder points at {copy}, not the addon's copy in {folder}");
        }

        return Failures(
            problems,
            text is not null && !text.Contains("script =", StringComparison.Ordinal) ? "" : $"the copy holds a script: [{text}]"
        );
    }

    // The placeholder's strip gives exactly the one message, and the node keeps its path.
    private static string PlaceholderRefusedWith(string placeholder, string expected)
    {
        (ConversionIssue[] issues, TscnDocument stripped, _, _) = PlaceholderStrip(placeholder);
        string[] messages = [.. issues.Select(issue => issue.Message)];
        return Failures(
            messages.SequenceEqual([expected], StringComparer.Ordinal) ? "" : $"the issues are [{string.Join("; ", messages)}], not [{expected}]",
            stripped.Nodes.Single().InstancePlaceholder == placeholder
                ? ""
                : $"the placeholder points at {stripped.Nodes.Single().InstancePlaceholder}, not {placeholder}"
        );
    }

    // A placeholder to a missing file stays as written, with no issue and no copy.
    private static string PlaceholderKept(string placeholder)
    {
        DeleteFiles(placeholder);
        return PlaceholderUnchanged(placeholder);
    }

    // The placeholder stays as written, with no issue.
    private static string PlaceholderUnchanged(string placeholder)
    {
        (ConversionIssue[] issues, TscnDocument stripped, _, _) = PlaceholderStrip(placeholder);
        return Failures(
            issues.Length == 0 ? "" : $"the issues are [{string.Join("; ", issues.Select(issue => issue.Message))}]",
            stripped.Nodes.Single().InstancePlaceholder == placeholder
                ? ""
                : $"the placeholder points at {stripped.Nodes.Single().InstancePlaceholder}, not {placeholder}"
        );
    }

    // A scene named twice, by an ext_resource and by a placeholder whose path climbs out and back, is copied once: both
    // point at the same copy.
    private static string PlaceholderCopiedOnce(string scene, string climb)
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "PackedScene",
                    Path = scene,
                    Id = "1_scene",
                },
            ],
            Nodes =
            [
                new TscnNode
                {
                    Name = "P",
                    Type = "Control",
                    InstancePlaceholder = climb,
                },
            ],
        };
        string folder = AddonTempFolder.Get();
        string[] before = Directory.GetFiles(folder);
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        string[] written = [.. Directory.GetFiles(folder).Except(before, StringComparer.Ordinal)];
        string? copy = stripped.Nodes.Single().InstancePlaceholder;
        return Failures(
            instances.Issues.Count == 0 ? "" : $"the issues are [{string.Join("; ", instances.Issues.Select(issue => issue.Message))}]",
            stripped.ExtResources.Single().Path == copy
                ? ""
                : $"the ext_resource points at {stripped.ExtResources.Single().Path}, the placeholder at {copy}",
            written.Length == 1 ? "" : $"the strip wrote [{string.Join(", ", written)}]"
        );
    }

    // A placeholder inside an instanced scene is redirected in that scene's copy too.
    private static string PlaceholderNested(string outer, string scene)
    {
        File.WriteAllText(
            Globalized(outer),
            "[gd_scene format=3]\n\n"
                + "[node name=\"Root\" type=\"Control\"]\n\n"
                + $"[node name=\"P\" type=\"Control\" parent=\".\" instance_placeholder=\"{scene}\"]\n"
        );
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "PackedScene",
                    Path = outer,
                    Id = "1_outer",
                },
            ],
            Nodes = [new TscnNode { Name = "Inst", Instance = new GodotResourceRef(GodotRefKind.External, "1_outer") }],
        };
        string folder = AddonTempFolder.Get();
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        string? copy = stripped.ExtResources.SingleOrDefault()?.Path;
        bool redirected = copy is { } path && path.StartsWith(folder, StringComparison.Ordinal);
        string text = redirected ? File.ReadAllText(copy!) : "";
        return Failures(
            instances.Issues.Count == 0 ? "" : $"the issues are [{string.Join("; ", instances.Issues.Select(issue => issue.Message))}]",
            redirected ? "" : $"the instance points at {copy}, not the addon's copy in {folder}",
            text.Contains($"instance_placeholder=\"{folder}", StringComparison.Ordinal)
                ? ""
                : $"the copy's placeholder is not the addon's copy: [{text}]"
        );
    }

    // A connection from the root into a child of the placeholder's scene that lost its script is dropped, as one into an
    // instance's is; the placeholder must have been redirected for the check to mean anything.
    private static string PlaceholderConnectionDropped(string childScene)
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            Nodes =
            [
                new TscnNode { Name = "Root", Type = "Control" },
                new TscnNode
                {
                    Name = "P",
                    Parent = ".",
                    InstancePlaceholder = childScene,
                },
            ],
            Connections =
            [
                new TscnConnection
                {
                    Signal = "poked",
                    From = ".",
                    To = "P/Child",
                    Method = "hide",
                },
            ],
        };
        using var instances = new XscnPreviewInstances();
        TscnDocument stripped = instances.Stripped(document);
        string? copy = stripped.Nodes.Single(node => node.Name == "P").InstancePlaceholder;
        return Failures(
            instances.Issues.Count == 0 ? "" : $"the issues are [{string.Join("; ", instances.Issues.Select(issue => issue.Message))}]",
            copy?.StartsWith(AddonTempFolder.Get(), StringComparison.Ordinal) == true
                ? ""
                : "the placeholder was not redirected, so nothing was tested",
            stripped.Connections.IsEmpty ? "" : $"the connection was kept: {stripped.Connections.Single().To}"
        );
    }

    // Keeps a project setting as found and puts it back on dispose; a Nil value erases it, so a run that had none leaves none.
    private sealed class PreviewSettingScope(string setting = PreviewScriptsSetting) : IDisposable
    {
        private readonly Variant previous = ProjectSettings.HasSetting(setting) ? ProjectSettings.GetSetting(setting) : default;

        public void Dispose()
        {
            ProjectSettings.SetSetting(setting, previous);
            previous.Dispose();
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

    // A VBoxContainer root holding a row of two Buttons, a MarginContainer with a uniquely named Label, a plain Node with a
    // Button, a SubViewportContainer whose SubViewport holds a Button, and last a hidden Panel holding a Button. The Panel
    // asks for the full rect, though the VBoxContainer places its children and skips a hidden one; it is there to be hidden.
    private const string PreviewRectsScene =
        "<VBoxContainer Name=\"Root\">\n"
        + "  <HBoxContainer Name=\"Row\">\n"
        + "    <Button Name=\"Ok\" Text=\"Ok\" />\n"
        + "    <Button Name=\"Cancel\" Text=\"Cancel\" />\n"
        + "  </HBoxContainer>\n"
        + "  <MarginContainer Name=\"Margin\">\n"
        + "    <Label Name=\"%Caption\" Text=\"Caption\" />\n"
        + "  </MarginContainer>\n"
        + "  <Node Name=\"Plain\">\n"
        + "    <Button Name=\"Loose\" Text=\"Loose\" />\n"
        + "  </Node>\n"
        + "  <SubViewportContainer Name=\"Inset\">\n"
        + "    <SubViewport Name=\"View\">\n"
        + "      <Button Name=\"Far\" Text=\"Far\" />\n"
        + "    </SubViewport>\n"
        + "  </SubViewportContainer>\n"
        + "  <Panel Name=\"Cover\" Visible=\"False\" AnchorRight=\"1\" AnchorBottom=\"1\">\n"
        + "    <Button Name=\"Covered\" Text=\"Covered\" />\n"
        + "  </Panel>\n"
        + "</VBoxContainer>\n";

    // A Control root holding an instance of a Control scene with one Button, to which an override adds a Label.
    private const string PreviewRectsInstanceScene =
        "<Control xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" Name=\"Root\">\n"
        + "  <x:Instance Scene=\"res://checks/cases/preview_rect_child.xscn\" Name=\"Inst\">\n"
        + "    <x:Override Path=\"Inner\">\n"
        + "      <Label Name=\"Added\" />\n"
        + "    </x:Override>\n"
        + "  </x:Instance>\n"
        + "</Control>\n";

    // The instance's root and the node inside it select the x:Instance element; the node the override adds, its own.
    private static readonly (string Path, string Start)[] PreviewRectsInstanceSpans =
    [
        ("Inst", "<x:Instance"),
        ("Inst/Inner", "<x:Instance"),
        ("Inst/Inner/Added", "<Label"),
    ];

    // PreviewRectsScene's Controls in pre-order, with their classes. The plain Node gets no entry, its Button does; the
    // SubViewportContainer gets one, but nothing inside its SubViewport; the hidden Panel and its Button get none.
    private static readonly (string Path, string Class)[] PreviewRectsExpected =
    [
        (".", "VBoxContainer"),
        ("Row", "HBoxContainer"),
        ("Row/Ok", "Button"),
        ("Row/Cancel", "Button"),
        ("Margin", "MarginContainer"),
        ("Margin/Caption", "Label"),
        ("Plain/Loose", "Button"),
        ("Inset", "SubViewportContainer"),
    ];

    // Instantiates the text, lays it out in a 640x480 SubViewport under the checks node and assesses its rectangles.
    // Container sorting is deferred (Container::queue_sort calls _sort_children deferred, scene/gui/container.cpp), so
    // the collect waits two frames for layout to settle.
    private async Task<string> WithPreviewRects(string text, Func<string, ImmutableArray<PreviewRect>, string> assess)
    {
        PreviewScene preview = XscnPreviewScene.Instantiate(text, PreviewPath, scriptsRequested: false);
        if (preview.Root is null || preview.Document is null)
        {
            preview.Root?.Free();
            return $"the preview has no root or no document: [{PreviewProblems(preview)}]";
        }

        var viewport = new SubViewport { Size = new Vector2I(640, 480) };
        viewport.AddChild(preview.Root);
        try
        {
            AddChild(viewport);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return assess(text, XscnPreviewRects.Collect(preview.Root, preview.Document));
        }
        finally
        {
            viewport.Free();
        }
    }

    // The rectangles follow the tree in pre-order, each Control child inside its Control parent, each with its element's span.
    private Task<string> PreviewRectsFollowTheTreeAndSpans() =>
        WithPreviewRects(
            PreviewRectsScene,
            (text, rects) =>
            {
                var failures = new List<string>();
                string paths = string.Join(", ", rects.Select(rect => rect.Path));
                string expectedPaths = string.Join(", ", PreviewRectsExpected.Select(expected => expected.Path));
                if (paths != expectedPaths)
                {
                    failures.Add($"paths [{paths}], expected [{expectedPaths}]");
                }

                string classes = string.Join(", ", rects.Select(rect => rect.Class));
                string expectedClasses = string.Join(", ", PreviewRectsExpected.Select(expected => expected.Class));
                if (classes != expectedClasses)
                {
                    failures.Add($"classes [{classes}], expected [{expectedClasses}]");
                }

                failures.AddRange(rects.Where(rect => rect.Kind != "control").Select(rect => $"{rect.Path} has kind {rect.Kind}"));
                var byPath = rects.ToDictionary(rect => rect.Path, StringComparer.Ordinal);
                foreach (PreviewRect rect in rects.Where(rect => rect.Path != "."))
                {
                    int slash = rect.Path.LastIndexOf('/');
                    string parentPath = slash < 0 ? "." : rect.Path[..slash];
                    // A parent with no entry is not a Control (Plain), so its child's rect has nothing to lie inside. The
                    // parent's rect grows by one pixel to allow for rounding in the layout's float positions.
                    if (byPath.TryGetValue(parentPath, out PreviewRect? parent) && !parent.Rect.Grow(1f).Encloses(rect.Rect))
                    {
                        failures.Add($"{rect.Path} {rect.Rect} is not inside {parent.Path} {parent.Rect}");
                    }

                    failures.Add(PreviewRectSpanFailure(text, rect, rect.Class, rect.Path[(slash + 1)..]));
                }

                if (byPath.TryGetValue(".", out PreviewRect? root))
                {
                    failures.Add(PreviewRectSpanFailure(text, root, root.Class, "Root"));
                }

                return Failures([.. failures]);
            }
        );

    // The entry's span starts with the element's tag and holds its Name attribute, with or without a unique name's %.
    private static string PreviewRectSpanFailure(string text, PreviewRect rect, string tag, string name)
    {
        string source = text.Substring(rect.SpanStart, rect.SpanLength);
        bool named = source.Contains($"Name=\"{name}\"", StringComparison.Ordinal) || source.Contains($"Name=\"%{name}\"", StringComparison.Ordinal);
        return source.StartsWith("<" + tag, StringComparison.Ordinal) && named
            ? ""
            : $"{rect.Path} has span [{source}], not a <{tag}> element named {name}";
    }

    // A node inside an instanced scene, and the instance's own root, both select the x:Instance element; a node an override
    // adds selects its own element.
    private Task<string> PreviewRectsSelectTheInstanceElement() =>
        WithPreviewRects(
            PreviewRectsInstanceScene,
            (text, rects) =>
                Failures([
                    .. PreviewRectsInstanceSpans.Select(expected =>
                        rects.FirstOrDefault(rect => rect.Path == expected.Path) switch
                        {
                            null => $"no entry for {expected.Path} among [{string.Join(", ", rects.Select(rect => rect.Path))}]",
                            var rect when text.Substring(rect.SpanStart, rect.SpanLength).StartsWith(expected.Start, StringComparison.Ordinal) => "",
                            var rect =>
                                $"{expected.Path} selects [{text.Substring(rect.SpanStart, rect.SpanLength)}], not a {expected.Start} element",
                        }
                    ),
                ])
        );

    // Collecting from a root outside the tree throws rather than returning rectangles no layout produced.
    private static string PreviewRectsNeedATree()
    {
        PreviewScene preview = XscnPreviewScene.Instantiate(CleanScene, PreviewPath, scriptsRequested: false);
        try
        {
            if (preview.Root is null || preview.Document is null)
            {
                return $"the preview has no root or no document: [{PreviewProblems(preview)}]";
            }

            try
            {
                _ = XscnPreviewRects.Collect(preview.Root, preview.Document);
                return "Collect returned for a root outside the tree";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }
        finally
        {
            preview.Root?.Free();
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

    // GXAML-167: the preview server, in a discovery folder of its own under user://, talked to by a same-process client on
    // the thread pool while the frame loop pumps it.
    private const string PreviewDiscoveryFolder = "user://gx-preview-discovery/";

    // A hang guard only: a passing check never waits it out.
    private const ulong PreviewHangGuardMs = 60_000;

    // Two Controls: a VBoxContainer root and its Button.
    private const string PreviewServerScene = "<VBoxContainer Name=\"Root\">\n  <Button Name=\"Ok\" Text=\"Ok\" />\n</VBoxContainer>\n";

    // The same scene without its end tag, which does not parse.
    private const string PreviewBrokenScene = "<VBoxContainer Name=\"Root\">\n  <Button Name=\"Ok\" Text=\"Ok\" />\n";

    // Frames the server closes the connection on without a reply.
    private static readonly (string What, string Frame)[] PreviewMalformedFrames =
    [
        ("a header over 1 KiB", "X-Pad: " + new string('a', 1100) + "\r\nContent-Length: 2\r\n\r\n{}"),
        ("a non-digit Content-Length", "Content-Length: 1a\r\n\r\n{"),
        ("an HTTP request line", "POST / HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\n\r\n{}"),
    ];

    // A rectangle's fields in a render result.
    private static readonly string[] PreviewRectFields = ["x", "y", "width", "height"];

    // What a check's client asks of the frame loop: hold the pump, or stop the server, both on the main thread.
    private sealed class PreviewDriver
    {
        private TaskCompletionSource? pauseAcknowledged;

        private volatile bool paused;

        internal bool Paused
        {
            get => paused;
            set => paused = value;
        }

        internal bool StopAsked { get; set; }

        // The size of each SubViewport seen under the checks node, read each frame on the main thread.
        internal List<Vector2I> ViewportSizes { get; } = [];

        // The 2D size override of each SubViewport seen under the checks node, read with its size.
        internal List<Vector2I> ViewportOverrides { get; } = [];

        // Holds the pump; the task completes once the frame loop has seen the hold, so nothing the client sends after it
        // can begin a render.
        internal Task PauseAsync()
        {
            var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Threading.Volatile.Write(ref pauseAcknowledged, acknowledged);
            paused = true;
            return acknowledged.Task;
        }

        private TaskCompletionSource? frameWaiter;

        // Completes once the frame loop has run one more frame; a client's event wait, never a timed one.
        internal Task NextFrameAsync()
        {
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Threading.Volatile.Write(ref frameWaiter, waiter);
            return waiter.Task;
        }

        // Called by the frame loop on the main thread after each pump, and once after the server is disposed.
        internal void Tick() => System.Threading.Interlocked.Exchange(ref frameWaiter, null)?.TrySetResult();

        // Called by the frame loop on the main thread before it decides whether to pump.
        internal void AcknowledgePause()
        {
            if (paused)
            {
                System.Threading.Interlocked.Exchange(ref pauseAcknowledged, null)?.TrySetResult();
            }
        }
    }

    // A client of the preview server; it never calls Godot, since it runs on the thread pool.
    private sealed class PreviewClient : IDisposable
    {
        private readonly TcpClient client = new();

        private XscnPreviewFrameReader? reader;

        // The client's own port, which is the server's remote port for this connection.
        internal int LocalPort { get; private set; }

        internal static async Task<PreviewClient> ConnectAsync(int port)
        {
            var preview = new PreviewClient();
            try
            {
                await preview.client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                preview.LocalPort = ((IPEndPoint)preview.client.Client.LocalEndPoint!).Port;
                preview.reader = new XscnPreviewFrameReader(preview.client.GetStream());
                return preview;
            }
            catch
            {
                preview.Dispose();
                throw;
            }
        }

        internal Task SendAsync(string json) => XscnPreviewFraming.WriteAsync(client.GetStream(), Encoding.UTF8.GetBytes(json), default);

        // A server that closed the connection first may refuse the rest of the bytes, which is no failure here.
        internal async Task SendRawAsync(byte[] bytes)
        {
            try
            {
                await client.GetStream().WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (IOException) { }
        }

        // The next message, or null once the server closed the connection.
        internal async Task<JsonElement?> ReceiveAsync()
        {
            try
            {
                byte[]? body = await reader!.ReadAsync(XscnPreviewFraming.MaxAuthenticatedBody, default).ConfigureAwait(false);
                if (body is null)
                {
                    return null;
                }

                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (IOException)
            {
                return null;
            }
        }

        // The messages up to those with the ids, keyed by their ids' JSON text, added to the given ones; fewer when the
        // server closes the connection first.
        internal async Task<Dictionary<string, JsonElement>> ReceiveThroughAsync(int[] ids, Dictionary<string, JsonElement>? into = null)
        {
            Dictionary<string, JsonElement> replies = into ?? [];
            while (!ids.All(id => replies.ContainsKey(PreviewIdText(id))) && await ReceiveAsync().ConfigureAwait(false) is { } message)
            {
                replies[message.TryGetProperty("id", out JsonElement id) ? id.GetRawText() : "null"] = message;
            }

            return replies;
        }

        public void Dispose() => client.Dispose();
    }

    private static string PreviewIdText(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string PreviewJson(string value) => "\"" + JsonEncodedText.Encode(value) + "\"";

    private static string PreviewPingJson(int id, string token, string project) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"ping\",\"params\":{{\"token\":{PreviewJson(token)},\"project\":{PreviewJson(project)}}}}}";

    private static string PreviewBarePingJson(int id) => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"ping\"}}";

    private static string PreviewRenderJson(int id, string document, string text, int width, int height) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"render\",\"params\":{{\"document\":{PreviewJson(document)},"
        + $"\"text\":{PreviewJson(text)},\"width\":{width},\"height\":{height}}}}}";

    // A render's parameters, its scale as raw JSON text.
    private sealed record PreviewScaledRender(string Document, string Text, int Width, int Height, string Scale);

    // The scene at 300x200 at scale 1.
    private static readonly PreviewScaledRender PreviewLayoutRender = new(PreviewPath, PreviewServerScene, 300, 200, "1");

    // The scene at 4x4 at scale 1, for the renders whose image comes from the seam.
    private static readonly PreviewScaledRender PreviewTinyRender = new(PreviewPath, PreviewServerScene, 4, 4, "1");

    private static string PreviewScaledRenderJson(int id, PreviewScaledRender render) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"render\",\"params\":{{\"document\":{PreviewJson(render.Document)},"
        + $"\"text\":{PreviewJson(render.Text)},\"width\":{render.Width},\"height\":{render.Height},\"scale\":{render.Scale}}}}}";

    private static string PreviewSceneRenderJson(int id) => PreviewRenderJson(id, PreviewPath, PreviewServerScene, 64, 64);

    private static string PreviewCancelJson(int id) => $"{{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{{\"id\":{id}}}}}";

    private static string PreviewDescribe(JsonElement? message) => message?.GetRawText() ?? "(the connection closed)";

    private static JsonElement? PreviewReply(Dictionary<string, JsonElement> replies, int id) =>
        replies.TryGetValue(PreviewIdText(id), out JsonElement message) ? message : null;

    private static int? PreviewErrorCode(JsonElement? message) =>
        message is { ValueKind: JsonValueKind.Object } reply && reply.TryGetProperty("error", out JsonElement error)
            ? error.GetProperty("code").GetInt32()
            : null;

    private static string PreviewErrorFailure(Dictionary<string, JsonElement> replies, int id, int code, string what)
    {
        JsonElement? reply = PreviewReply(replies, id);
        return PreviewErrorCode(reply) == code ? "" : $"{what} got {PreviewDescribe(reply)}, not error {code}";
    }

    private static string PreviewLoadedFailure(Dictionary<string, JsonElement> replies, int id, string what)
    {
        JsonElement? reply = PreviewReply(replies, id);
        bool loaded = reply is { } message && message.TryGetProperty("result", out JsonElement result) && result.GetProperty("loaded").GetBoolean();
        return loaded ? "" : $"{what} got {PreviewDescribe(reply)}, not a loaded result";
    }

    // Starts a server with a discovery folder of its own and the checks node as its host, runs the client on the thread
    // pool while the frame loop pumps the server, then stops it.
    private async Task<string> WithPreviewServer(
        Func<XscnPreviewServer, PreviewDriver, Task<string>> client,
        Func<PreviewDriver, string>? after = null
    )
    {
        var server = new XscnPreviewServer(Globalized($"{PreviewDiscoveryFolder}{Guid.NewGuid():N}"), Globalized("res://"), this);
        var driver = new PreviewDriver();
        try
        {
            if (!server.Start())
            {
                return "the server did not start";
            }

            string failure = await PumpedUntil(server, driver, Task.Run(() => client(server, driver)));
            return Failures(failure, after is null ? "" : after(driver));
        }
        finally
        {
            server.Dispose();
            // A client waiting on a frame after the hang guard fired sees the stopped server's empty store and ends.
            driver.Tick();
            DeleteFolder(PreviewDiscoveryFolder);
        }
    }

    // Pumps the server once a frame until the client is done; the main thread never blocks on the client.
    private async Task<string> PumpedUntil(XscnPreviewServer server, PreviewDriver driver, Task<string> client)
    {
        ulong deadline = Time.GetTicksMsec() + PreviewHangGuardMs;
        while (!client.IsCompleted)
        {
            if (Time.GetTicksMsec() > deadline)
            {
                return "hung";
            }

            if (driver.StopAsked)
            {
                server.Stop();
            }

            driver.AcknowledgePause();
            if (!driver.Paused)
            {
                server.Pump();
            }

            driver.ViewportSizes.AddRange(GetChildren().OfType<SubViewport>().Select(viewport => viewport.Size));
            driver.ViewportOverrides.AddRange(GetChildren().OfType<SubViewport>().Select(viewport => viewport.Size2DOverride));
            driver.Tick();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return await client;
    }

    private static async Task<PreviewClient> OpenPreviewClientAsync(XscnPreviewServer server, string project)
    {
        PreviewClient client = await PreviewClient.ConnectAsync(server.Port).ConfigureAwait(false);
        await client.SendAsync(PreviewPingJson(0, server.Token, project)).ConfigureAwait(false);
        JsonElement? reply = await client.ReceiveAsync().ConfigureAwait(false);
        if (reply is { } message && message.TryGetProperty("result", out _))
        {
            return client;
        }

        client.Dispose();
        throw new InvalidOperationException($"the ping was refused: {PreviewDescribe(reply)}");
    }

    // A right ping answers the protocol, the editor's pid and its normalized project.
    private static async Task<string> PreviewPingAnswersFailure(XscnPreviewServer server, string project)
    {
        using PreviewClient client = await PreviewClient.ConnectAsync(server.Port).ConfigureAwait(false);
        await client.SendAsync(PreviewPingJson(1, server.Token, project)).ConfigureAwait(false);
        JsonElement? reply = await client.ReceiveAsync().ConfigureAwait(false);
        if (reply is not { } message || !message.TryGetProperty("result", out JsonElement result))
        {
            return $"the right ping got {PreviewDescribe(reply)}";
        }

        bool right =
            result.GetProperty("protocol").GetInt32() == 1
            && result.GetProperty("pid").GetInt32() == System.Environment.ProcessId
            && result.GetProperty("project").GetString() == server.Project;
        return right ? "" : $"the right ping got {result.GetRawText()}, not protocol 1, pid {System.Environment.ProcessId}, project {server.Project}";
    }

    // A refused first request answers the error, naming each given text in its message, and the connection is closed.
    private static async Task<string> PreviewRefusedFirstFailure(XscnPreviewServer server, string what, string json, int code, params string[] named)
    {
        using PreviewClient client = await PreviewClient.ConnectAsync(server.Port).ConfigureAwait(false);
        await client.SendAsync(json).ConfigureAwait(false);
        JsonElement? reply = await client.ReceiveAsync().ConfigureAwait(false);
        JsonElement? after = await client.ReceiveAsync().ConfigureAwait(false);
        string message = PreviewErrorCode(reply) is null ? "" : reply!.Value.GetProperty("error").GetProperty("message").GetString() ?? "";
        return Failures(
            PreviewErrorCode(reply) == code ? "" : $"{what} got {PreviewDescribe(reply)}, not error {code}",
            named.All(name => message.Contains(name, StringComparison.Ordinal))
                ? ""
                : $"{what}'s message [{message}] does not name [{string.Join(", ", named)}]",
            after is null ? "" : $"{what} left the connection open: it then got {PreviewDescribe(after)}"
        );
    }

    private static async Task<string> PreviewClosedWithoutReplyFailure(XscnPreviewServer server, string what, byte[] frame)
    {
        using PreviewClient client = await PreviewClient.ConnectAsync(server.Port).ConfigureAwait(false);
        await client.SendRawAsync(frame).ConfigureAwait(false);
        JsonElement? reply = await client.ReceiveAsync().ConfigureAwait(false);
        return reply is null ? "" : $"{what} got {PreviewDescribe(reply)} rather than a closed connection";
    }

    // C139: one discovery file with the server's values once it starts, none once it stops; a second stop changes nothing.
    private string PreviewServerWritesAndRemovesItsDiscoveryFile()
    {
        string folder = Globalized($"{PreviewDiscoveryFolder}{Guid.NewGuid():N}");
        var server = new XscnPreviewServer(folder, Globalized("res://"), this);
        try
        {
            if (!server.Start())
            {
                return "the server did not start";
            }

            string written = PreviewDiscoveryFailure(folder, server);
            server.Stop();
            string removed = PreviewFolderEmptyFailure(folder, "after the stop");
            server.Stop();
            return Failures(written, removed, PreviewFolderEmptyFailure(folder, "after a second stop"));
        }
        finally
        {
            server.Dispose();
            DeleteFolder(PreviewDiscoveryFolder);
        }
    }

    private static string PreviewDiscoveryFailure(string folder, XscnPreviewServer server)
    {
        string[] files = [.. Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>()];
        string prefix = $"{System.Environment.ProcessId}-";
        if (files.Length != 1 || !IsPreviewDiscoveryName(files[0], prefix))
        {
            return $"the folder holds [{string.Join(", ", files)}], not one {prefix}<32 hex>.json";
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, files[0])));
        JsonElement root = document.RootElement;
        string names = string.Join(", ", root.EnumerateObject().Select(property => property.Name));
        return names == "port, token, pid, project"
            ? PreviewDiscoveryValuesFailure(root, server)
            : $"the file holds [{names}], not [port, token, pid, project]";
    }

    private static string PreviewDiscoveryValuesFailure(JsonElement root, XscnPreviewServer server)
    {
        bool same =
            root.GetProperty("port").GetInt32() == server.Port
            && root.GetProperty("token").GetString() == server.Token
            && root.GetProperty("pid").GetInt32() == System.Environment.ProcessId
            && root.GetProperty("project").GetString() == server.Project;
        return same
            ? ""
            : $"the file holds port {root.GetProperty("port")}, pid {root.GetProperty("pid")}, project {root.GetProperty("project")} and "
                + $"a token that {(root.GetProperty("token").GetString() == server.Token ? "matches" : "does not match")}; the server has "
                + $"port {server.Port}, pid {System.Environment.ProcessId}, project {server.Project}";
    }

    private static bool IsPreviewDiscoveryName(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal)
        && name.EndsWith(".json", StringComparison.Ordinal)
        && name.Length == prefix.Length + 32 + ".json".Length
        && name[prefix.Length..^".json".Length].All(char.IsAsciiHexDigitLower);

    private static string PreviewFolderEmptyFailure(string folder, string when) =>
        Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0
            ? $"{when} the folder holds [{string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName))}]"
            : "";

    // C140: the first request must be a ping with the token and the project.
    private Task<string> PreviewServerAuthenticatesTheFirstPing()
    {
        string project = Globalized("res://");
        string other = Path.Combine(Path.GetTempPath(), "gx-other-project");
        return WithPreviewServer(
            async (server, _) =>
                Failures(
                    await PreviewPingAnswersFailure(server, project).ConfigureAwait(false),
                    await PreviewRefusedFirstFailure(server, "a render first", PreviewSceneRenderJson(1), XscnPreviewProtocol.NotAuthenticated)
                        .ConfigureAwait(false),
                    await PreviewRefusedFirstFailure(
                            server,
                            "a wrong token",
                            PreviewPingJson(1, new string('0', 64), project),
                            XscnPreviewProtocol.NotAuthenticated
                        )
                        .ConfigureAwait(false),
                    await PreviewRefusedFirstFailure(
                            server,
                            "another project",
                            PreviewPingJson(1, server.Token, other),
                            XscnPreviewProtocol.OtherProject,
                            server.Project,
                            XscnPreviewServer.NormalizeProject(other)
                        )
                        .ConfigureAwait(false)
                )
        );
    }

    // C140, on a server of its own so no connection an earlier case closed still holds a slot: a fifth unauthenticated
    // connection is closed while four are open, and a ninth authenticated one while eight are. The accept loop admits in
    // order, so the four are counted before the fifth is.
    private Task<string> PreviewServerCapsConnections()
    {
        string project = Globalized("res://");
        return WithPreviewServer(
            async (server, _) =>
            {
                string ping = PreviewPingJson(1, server.Token, project);
                var open = new List<PreviewClient>();
                try
                {
                    for (int index = 0; index < 4; index++)
                    {
                        open.Add(await PreviewClient.ConnectAsync(server.Port).ConfigureAwait(false));
                    }

                    string fifth = await PreviewClosedWithoutReplyFailure(server, "a fifth unauthenticated connection", PreviewFrame(ping))
                        .ConfigureAwait(false);
                    string promoted = await PreviewPingAllFailure(open, ping).ConfigureAwait(false);
                    for (int index = 0; index < 4; index++)
                    {
                        open.Add(await OpenPreviewClientAsync(server, project).ConfigureAwait(false));
                    }

                    string ninth = await PreviewClosedWithoutReplyFailure(server, "a ninth authenticated connection", PreviewFrame(ping))
                        .ConfigureAwait(false);
                    return Failures(fifth, promoted, ninth);
                }
                finally
                {
                    open.ForEach(client => client.Dispose());
                }
            }
        );
    }

    // Authenticates each open connection.
    private static async Task<string> PreviewPingAllFailure(List<PreviewClient> open, string ping)
    {
        foreach (PreviewClient client in open)
        {
            await client.SendAsync(ping).ConfigureAwait(false);
            JsonElement? reply = await client.ReceiveAsync().ConfigureAwait(false);
            if (reply is not { } message || !message.TryGetProperty("result", out _))
            {
                return $"an unauthenticated connection's ping got {PreviewDescribe(reply)}";
            }
        }

        return "";
    }

    private static byte[] PreviewFrame(string json) => XscnPreviewFraming.Frame(Encoding.UTF8.GetBytes(json));

    // C141: malformed frames close their connections, a batch and a repeated token key are refused, and a good connection
    // still works after them.
    private Task<string> PreviewServerRefusesMalformedFrames()
    {
        string project = Globalized("res://");
        return WithPreviewServer(
            async (server, _) =>
            {
                string ping = PreviewPingJson(1, server.Token, project);
                // The right token second, so a parser that kept the last key would let the ping in.
                string duplicate =
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{\"token\":\"wrong\","
                    + $"\"token\":{PreviewJson(server.Token)},\"project\":{PreviewJson(project)}}}}}";
                var failures = new List<string>();
                foreach ((string what, string frame) in PreviewMalformedFrames)
                {
                    failures.Add(await PreviewClosedWithoutReplyFailure(server, what, Encoding.ASCII.GetBytes(frame)).ConfigureAwait(false));
                }

                // A right ping padded with JSON whitespace to one byte over the cap: without the cap it would be answered.
                string padded = ping + new string(' ', XscnPreviewFraming.MaxUnauthenticatedBody + 1 - ping.Length);
                failures.Add(
                    await PreviewClosedWithoutReplyFailure(server, "a 4097-byte ping before authentication", PreviewFrame(padded))
                        .ConfigureAwait(false)
                );

                failures.Add(
                    await PreviewRefusedFirstFailure(server, "a batch", $"[{ping}]", XscnPreviewProtocol.InvalidRequest).ConfigureAwait(false)
                );
                failures.Add(
                    await PreviewRefusedFirstFailure(server, "a repeated token key", duplicate, XscnPreviewProtocol.ParseError).ConfigureAwait(false)
                );
                failures.Add(await PreviewPingAnswersFailure(server, project).ConfigureAwait(false));
                return Failures([.. failures]);
            }
        );
    }

    // C142: a render answers the rectangles a direct collect of the same text gives, an invalid path is refused, and a
    // render over the cap gets a clamped SubViewport.
    private async Task<string> PreviewServerRendersRects()
    {
        ImmutableArray<PreviewRect> direct = [];
        string directFailure = await WithPreviewRects(
            PreviewServerScene,
            (_, rects) =>
            {
                direct = rects;
                return "";
            }
        );
        if (directFailure.Length > 0)
        {
            return $"the direct collect failed: {directFailure}";
        }

        string[] expected =
        [
            .. direct.Select(rect =>
                PreviewRectText(
                    rect.Path,
                    rect.Class,
                    rect.Kind,
                    [rect.Rect.Position.X, rect.Rect.Position.Y, rect.Rect.Size.X, rect.Rect.Size.Y],
                    rect.SpanStart,
                    rect.SpanLength
                )
            ),
        ];
        string project = Globalized("res://");
        const string invalidPath = "res://checks/../checks/cases/preview.xscn";
        const string backslashPath = "res://checks\\cases/preview.xscn";
        const string nulPath = "res://checks/cases/pre\0view.xscn";
        return await WithPreviewServer(
            async (server, driver) =>
            {
                using PreviewClient client = await OpenPreviewClientAsync(server, project).ConfigureAwait(false);
                await client.SendAsync(PreviewRenderJson(2, PreviewPath, PreviewServerScene, 640, 480)).ConfigureAwait(false);
                await client.SendAsync(PreviewRenderJson(3, invalidPath, PreviewServerScene, 640, 480)).ConfigureAwait(false);
                await client.SendAsync(PreviewRenderJson(5, backslashPath, PreviewServerScene, 640, 480)).ConfigureAwait(false);
                await client.SendAsync(PreviewRenderJson(6, nulPath, PreviewServerScene, 640, 480)).ConfigureAwait(false);
                await client
                    .SendAsync(PreviewRenderJson(4, CasesPath + "preview_large.xscn", PreviewServerScene, 10000, 10000))
                    .ConfigureAwait(false);
                Dictionary<string, JsonElement> replies = await client.ReceiveThroughAsync([2, 3, 4, 5, 6]).ConfigureAwait(false);
                return Failures(
                    PreviewRectsReplyFailure(PreviewReply(replies, 2), expected),
                    PreviewErrorFailure(replies, 3, XscnPreviewProtocol.InvalidDocument, $"a render of {invalidPath}"),
                    PreviewErrorFailure(replies, 5, XscnPreviewProtocol.InvalidDocument, "a render of a path with a backslash"),
                    PreviewErrorFailure(replies, 6, XscnPreviewProtocol.InvalidDocument, "a render of a path with a NUL"),
                    PreviewLoadedFailure(replies, 4, "the 10000x10000 render"),
                    await PreviewScaledFailure(client).ConfigureAwait(false),
                    await PreviewUndrawnFailure(server, project).ConfigureAwait(false),
                    await PreviewStoredImageFailure(server, driver, project).ConfigureAwait(false)
                );
            },
            driver =>
                Failures(
                    driver.ViewportSizes.Contains(new Vector2I(XscnPreviewServer.MaxSide, XscnPreviewServer.MaxSide))
                        ? ""
                        : $"no SubViewport of 4096x4096 was seen, only [{string.Join(", ", driver.ViewportSizes.Distinct())}]",
                    driver.ViewportSizes.Contains(new Vector2I(600, 400)) && driver.ViewportOverrides.Contains(new Vector2I(300, 200))
                        ? ""
                        : "no SubViewport of 600x400 with a 300x200 override was seen, only sizes "
                            + $"[{string.Join(", ", driver.ViewportSizes.Distinct())}] and overrides "
                            + $"[{string.Join(", ", driver.ViewportOverrides.Distinct())}]"
                )
        );
    }

    // Sends one render and waits for its reply.
    private static async Task<JsonElement?> PreviewRoundTripAsync(PreviewClient client, int id, string json)
    {
        await client.SendAsync(json).ConfigureAwait(false);
        return PreviewReply(await client.ReceiveThroughAsync([id]).ConfigureAwait(false), id);
    }

    // C142, headless: the scene at 300x200 at scale 1 and at scale 2, one after the other so neither supersedes the other,
    // then text that does not parse; a scale below 1 or not a number is refused. Headless draws nothing, so no reply has an
    // image, and the broken text, with no image kept, is not stale.
    private static async Task<string> PreviewScaledFailure(PreviewClient client)
    {
        PreviewScaledRender layout = PreviewLayoutRender;
        JsonElement? one = await PreviewRoundTripAsync(client, 10, PreviewScaledRenderJson(10, layout)).ConfigureAwait(false);
        JsonElement? two = await PreviewRoundTripAsync(client, 11, PreviewScaledRenderJson(11, layout with { Scale = "2" })).ConfigureAwait(false);
        JsonElement? broken = await PreviewRoundTripAsync(
                client,
                12,
                PreviewScaledRenderJson(12, layout with { Text = PreviewBrokenScene, Scale = "2" })
            )
            .ConfigureAwait(false);
        await client.SendAsync(PreviewScaledRenderJson(13, layout with { Scale = "0.5" })).ConfigureAwait(false);
        await client.SendAsync(PreviewScaledRenderJson(14, layout with { Scale = "\"x\"" })).ConfigureAwait(false);
        Dictionary<string, JsonElement> refused = await client.ReceiveThroughAsync([13, 14]).ConfigureAwait(false);
        return Failures(
            PreviewDoubledFailure(one, two),
            PreviewImageFailure(one, "the scale-1 render", expectImage: false, expectStale: false),
            PreviewImageFailure(two, "the scale-2 render", expectImage: false, expectStale: false),
            PreviewBrokenFailure(broken, "the broken render after a good one"),
            PreviewImageFailure(broken, "the broken render after a good one", expectImage: false, expectStale: false),
            PreviewErrorFailure(refused, 13, XscnPreviewProtocol.InvalidParams, "a render at scale 0.5"),
            PreviewErrorFailure(refused, 14, XscnPreviewProtocol.InvalidParams, "a render at scale \"x\"")
        );
    }

    private static JsonElement[] PreviewResultRects(JsonElement? reply) =>
        reply is { } message && message.TryGetProperty("result", out JsonElement result) ? [.. result.GetProperty("rects").EnumerateArray()] : [];

    // Each rect of the scale-2 reply is twice the same rect of the scale-1 reply.
    private static string PreviewDoubledFailure(JsonElement? one, JsonElement? two)
    {
        JsonElement[] single = PreviewResultRects(one);
        JsonElement[] doubled = PreviewResultRects(two);
        bool same =
            single.Length > 0
            && single.Length == doubled.Length
            && single
                .Zip(doubled)
                .All(pair =>
                    pair.First.GetProperty("path").GetString() == pair.Second.GetProperty("path").GetString()
                    && PreviewRectFields.All(name => pair.Second.GetProperty(name).GetSingle() == 2 * pair.First.GetProperty(name).GetSingle())
                );
        return same ? "" : $"the scale-2 render got {PreviewDescribe(two)}, not twice the rects of the scale-1 render's {PreviewDescribe(one)}";
    }

    // Whether the reply's result carries an image and is stale as expected.
    private static string PreviewImageFailure(JsonElement? reply, string what, bool expectImage, bool expectStale)
    {
        if (reply is not { } message || !message.TryGetProperty("result", out JsonElement result))
        {
            return $"{what} got {PreviewDescribe(reply)}";
        }

        string found = $"{PreviewKindOf(result, "image")} {PreviewKindOf(result, "stale")}";
        string expected =
            $"{(expectImage ? JsonValueKind.String : JsonValueKind.Undefined)} {(expectStale ? JsonValueKind.True : JsonValueKind.False)}";
        return found == expected ? "" : $"{what} got {result.GetRawText()}, expected image and stale of the kinds {expected}, not {found}";
    }

    private static JsonValueKind PreviewKindOf(JsonElement result, string name) =>
        result.TryGetProperty(name, out JsonElement value) ? value.ValueKind : JsonValueKind.Undefined;

    // A render whose text does not parse: not loaded, with the parse's diagnostics.
    private static string PreviewBrokenFailure(JsonElement? reply, string what) =>
        reply is { } message
        && message.TryGetProperty("result", out JsonElement result)
        && !result.GetProperty("loaded").GetBoolean()
        && result.GetProperty("diagnostics").GetArrayLength() > 0
            ? ""
            : $"{what} got {PreviewDescribe(reply)}, not an unloaded result with diagnostics";

    // C142, the last good image: with the image seam giving a 4x4 image headless, a good render followed by a broken one
    // on one connection answers the good one's image again, stale; the store holds one entry for that connection until
    // it closes, and a second connection's entry is gone once the server stops. The seam is restored after.
    private static async Task<string> PreviewStoredImageFailure(XscnPreviewServer server, PreviewDriver driver, string project)
    {
        // Each read of the drawn-frame count is one more frame, so every draw asked for has run by the next frame.
        ulong drawn = 0;
        server.FramesDrawnSource = () => ++drawn;
        server.ImageSource = _ => PreviewSeamImage();
        try
        {
            string first;
            using (PreviewClient one = await OpenPreviewClientAsync(server, project).ConfigureAwait(false))
            {
                JsonElement? good = await PreviewRoundTripAsync(one, 20, PreviewScaledRenderJson(20, PreviewTinyRender)).ConfigureAwait(false);
                JsonElement? broken = await PreviewRoundTripAsync(
                        one,
                        21,
                        PreviewScaledRenderJson(21, PreviewTinyRender with { Text = PreviewBrokenScene })
                    )
                    .ConfigureAwait(false);
                int port = one.LocalPort;
                int stored = server.StoredImages(port);
                first = Failures(
                    PreviewImageFailure(good, "the good render with the image seam", expectImage: true, expectStale: false),
                    PreviewImageFailure(broken, "the broken render after a good one with the image seam", expectImage: true, expectStale: true),
                    PreviewBrokenFailure(broken, "the broken render after a good one with the image seam"),
                    PreviewImageText(good) is { } image && image == PreviewImageText(broken)
                        ? ""
                        : $"the stale image is not the good render's: {PreviewDescribe(broken)} after {PreviewDescribe(good)}",
                    stored == 1 ? "" : $"the store holds {stored} images for the connection after its good render, not 1"
                );
                one.Dispose();
                await PreviewStoreEmptiedAsync(server, driver, port).ConfigureAwait(false);
            }

            using PreviewClient two = await OpenPreviewClientAsync(server, project).ConfigureAwait(false);
            JsonElement? kept = await PreviewRoundTripAsync(two, 22, PreviewScaledRenderJson(22, PreviewTinyRender)).ConfigureAwait(false);
            int before = server.StoredImages(two.LocalPort);
            // The frame loop stops the server on the main thread; the stop closes the connection.
            driver.StopAsked = true;
            JsonElement? closing = await two.ReceiveAsync().ConfigureAwait(false);
            int after = server.StoredImages(two.LocalPort);
            return Failures(
                first,
                PreviewImageFailure(kept, "the second connection's good render", expectImage: true, expectStale: false),
                before == 1 ? "" : $"the store holds {before} images for the second connection, not 1",
                closing is null ? "" : $"the second connection got {PreviewDescribe(closing)} rather than closing at the stop",
                after == 0 ? "" : $"the store holds {after} images for the second connection after the stop, not 0"
            );
        }
        finally
        {
            server.ImageSource = null;
            server.FramesDrawnSource = null;
        }
    }

    // C142, a draw that never runs: with the image seam set but the drawn-frame count never growing, the render waits out
    // its cap and answers with no image, and nothing is kept for its connection. The seams are restored after.
    private static async Task<string> PreviewUndrawnFailure(XscnPreviewServer server, string project)
    {
        server.FramesDrawnSource = () => 0;
        server.ImageSource = _ => PreviewSeamImage();
        try
        {
            using PreviewClient client = await OpenPreviewClientAsync(server, project).ConfigureAwait(false);
            JsonElement? reply = await PreviewRoundTripAsync(client, 30, PreviewScaledRenderJson(30, PreviewTinyRender)).ConfigureAwait(false);
            int stored = server.StoredImages(client.LocalPort);
            return Failures(
                PreviewImageFailure(reply, "a render whose draw never runs", expectImage: false, expectStale: false),
                stored == 0 ? "" : $"the store holds {stored} images after a render whose draw never ran, not 0"
            );
        }
        finally
        {
            server.ImageSource = null;
            server.FramesDrawnSource = null;
        }
    }

    private static string? PreviewImageText(JsonElement? reply) =>
        reply is { } message && message.TryGetProperty("result", out JsonElement result) && result.TryGetProperty("image", out JsonElement image)
            ? image.GetString()
            : null;

    // The connection leaves on the server's reader, so the store empties some frames after the client closes; waits frame
    // by frame until it has, and the hang guard ends a wait that never does.
    private static async Task PreviewStoreEmptiedAsync(XscnPreviewServer server, PreviewDriver driver, int port)
    {
        while (server.StoredImages(port) > 0)
        {
            await driver.NextFrameAsync().ConfigureAwait(false);
        }
    }

    // A 4x4 red image; the server calls the seam on the main thread and frees what it returns.
    private static Image PreviewSeamImage()
    {
        var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        image.Fill(Colors.Red);
        return image;
    }

    private static string PreviewRectText(string? path, string? engineClass, string? kind, float[] rect, int start, int length) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{path} {engineClass} {kind} ({rect[0]}, {rect[1]}, {rect[2]}, {rect[3]}) {start}+{length}"
        );

    private static string PreviewRectsReplyFailure(JsonElement? reply, string[] expected)
    {
        if (reply is not { } message || !message.TryGetProperty("result", out JsonElement result))
        {
            return $"the 640x480 render got {PreviewDescribe(reply)}";
        }

        string[] rects =
        [
            .. result
                .GetProperty("rects")
                .EnumerateArray()
                .Select(rect =>
                    PreviewRectText(
                        rect.GetProperty("path").GetString(),
                        rect.GetProperty("class").GetString(),
                        rect.GetProperty("kind").GetString(),
                        [.. PreviewRectFields.Select(name => rect.GetProperty(name).GetSingle())],
                        rect.GetProperty("spanStart").GetInt32(),
                        rect.GetProperty("spanLength").GetInt32()
                    )
                ),
        ];
        return Failures(
            result.GetProperty("loaded").GetBoolean() ? "" : "the 640x480 render was not loaded",
            expected.Length == 2 && rects.SequenceEqual(expected)
                ? ""
                : $"the render's rects are [{string.Join("; ", rects)}], expected the direct collect's two [{string.Join("; ", expected)}]"
        );
    }

    // C143: only the newest render per connection and document runs; a cancelled one, a seventeenth queued document and
    // one queued at the stop are answered with their errors. The pump is held, and the hold acknowledged by the frame
    // loop, before requests queue, and a ping after them proves they are queued.
    private Task<string> PreviewServerKeepsTheLatestRender()
    {
        string project = Globalized("res://");
        return WithPreviewServer(
            async (server, driver) =>
            {
                await driver.PauseAsync().ConfigureAwait(false);
                using PreviewClient one = await OpenPreviewClientAsync(server, project).ConfigureAwait(false);
                using PreviewClient two = await OpenPreviewClientAsync(server, project).ConfigureAwait(false);
                await one.SendAsync(PreviewSceneRenderJson(1)).ConfigureAwait(false);
                await one.SendAsync(PreviewSceneRenderJson(2)).ConfigureAwait(false);
                await one.SendAsync(PreviewBarePingJson(3)).ConfigureAwait(false);
                Dictionary<string, JsonElement> superseded = await one.ReceiveThroughAsync([1, 3]).ConfigureAwait(false);
                driver.Paused = false;
                await one.ReceiveThroughAsync([2], superseded).ConfigureAwait(false);

                await driver.PauseAsync().ConfigureAwait(false);
                await one.SendAsync(PreviewSceneRenderJson(4)).ConfigureAwait(false);
                await one.SendAsync(PreviewBarePingJson(5)).ConfigureAwait(false);
                Dictionary<string, JsonElement> first = await one.ReceiveThroughAsync([5]).ConfigureAwait(false);
                await two.SendAsync(PreviewSceneRenderJson(1)).ConfigureAwait(false);
                await two.SendAsync(PreviewBarePingJson(2)).ConfigureAwait(false);
                Dictionary<string, JsonElement> second = await two.ReceiveThroughAsync([2]).ConfigureAwait(false);
                driver.Paused = false;
                await one.ReceiveThroughAsync([4], first).ConfigureAwait(false);
                await two.ReceiveThroughAsync([1], second).ConfigureAwait(false);

                await driver.PauseAsync().ConfigureAwait(false);
                await one.SendAsync(PreviewSceneRenderJson(6)).ConfigureAwait(false);
                await one.SendAsync(PreviewCancelJson(6)).ConfigureAwait(false);
                await one.SendAsync(PreviewBarePingJson(7)).ConfigureAwait(false);
                Dictionary<string, JsonElement> cancelled = await one.ReceiveThroughAsync([6, 7]).ConfigureAwait(false);

                // Sixteen documents queued on one connection, then a seventeenth, which is refused at once.
                await one.SendAsync(PreviewSceneRenderJson(8)).ConfigureAwait(false);
                for (int index = 1; index <= 16; index++)
                {
                    string document = CasesPath + $"queued{index}.xscn";
                    await one.SendAsync(PreviewRenderJson(100 + index, document, PreviewServerScene, 64, 64)).ConfigureAwait(false);
                }

                await one.SendAsync(PreviewBarePingJson(9)).ConfigureAwait(false);
                Dictionary<string, JsonElement> stopped = await one.ReceiveThroughAsync([9]).ConfigureAwait(false);
                driver.StopAsked = true;
                await one.ReceiveThroughAsync([8], stopped).ConfigureAwait(false);
                return Failures(
                    PreviewErrorFailure(superseded, 1, XscnPreviewProtocol.Cancelled, "the superseded render"),
                    PreviewLoadedFailure(superseded, 2, "the newer render of the document"),
                    PreviewLoadedFailure(first, 4, "the first connection's render"),
                    PreviewLoadedFailure(second, 1, "the second connection's render of the same document"),
                    PreviewErrorFailure(cancelled, 6, XscnPreviewProtocol.Cancelled, "the cancelled render"),
                    PreviewErrorFailure(stopped, 116, XscnPreviewProtocol.Cancelled, "the seventeenth queued document"),
                    PreviewErrorFailure(stopped, 8, XscnPreviewProtocol.Stopping, "the render queued at the stop")
                );
            }
        );
    }
}
