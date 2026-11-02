using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <inheritdoc/>
    public override void _Ready()
    {
        (string Id, Func<string> Run)[] checks =
        [
            ("C1", CoreLoads),
            ("C2", ParsesInsideGodot),
            ("C3", Registered),
            ("C4", ParseErrorFailsTheLoad),
            ("C5", CustomSaverIsChosen),
            ("C6", AbsoluteOsPathLoads),
        ];
        int passed = 0;
        try
        {
            foreach ((string id, Func<string> run) in checks)
            {
                passed += Report(id, run) ? 1 : 0;
            }
        }
        finally
        {
            GD.Print($"DONE {passed}/{checks.Length}");
            GetTree().Quit(passed == checks.Length ? 0 : 1);
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
}
