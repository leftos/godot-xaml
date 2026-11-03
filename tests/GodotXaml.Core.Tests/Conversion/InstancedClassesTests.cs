using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// The way back reads the class of an instanced node, an inherited root or an override from the scene it comes from, so
// its properties come back in Godot's order; a scene it cannot read keeps source order and warns once per reference.
public sealed class InstancedClassesTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private const string SourceKey = "res://main.xscn";
    private const string TwoProperties = "GrowVertical=\"0\" LayoutMode=\"1\"";
    private const string ControlScene = "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n";
    private const string BoxScene =
        "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n\n[node name=\"Box\" type=\"Panel\" parent=\".\"]\n";

    private const string BrokenTscn = "[gd_scene format=3]\n\n[node name=\"Root\"\n";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly string[] LayoutFirst = ["layout_mode", "grow_vertical"];
    private static readonly string[] SourceOrder = ["grow_vertical", "layout_mode"];

    [Fact]
    public void AnInstanceTakesTheRootClassOfItsScene()
    {
        var files = new Files(("res://a.tscn", ControlScene));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", TwoProperties), files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void AnInstanceOfAnXscnTakesItsRootClass()
    {
        var files = new Files(("res://a.xscn", $"<Control {X} Name=\"Root\" />"));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.xscn", TwoProperties), files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void AnInheritedRootTakesItsBaseClass()
    {
        var files = new Files(("res://b.tscn", Inheriting("res://a.tscn")), ("res://a.tscn", ControlScene));

        ConversionResult<TscnDocument> back = Back($"<x:Instance {X} Name=\"Root\" Scene=\"res://b.tscn\" {TwoProperties} />", files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[0]));
    }

    [Fact]
    public void AnOverrideTakesTheClassOfItsNode()
    {
        var files = new Files(("res://a.tscn", BoxScene));

        ConversionResult<TscnDocument> back = Back(OverrideIn("res://a.tscn", "Box"), files);

        Assert.Empty(back.Issues);
        Assert.Equal("Box", back.Document.Nodes[2].Name);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[2]));
    }

    [Fact]
    public void AnOverrideCrossesANestedInstance()
    {
        string outer =
            "[gd_scene format=3]\n\n[ext_resource type=\"PackedScene\" path=\"res://b.tscn\" id=\"1_b\"]\n\n"
            + "[node name=\"Root\" type=\"Node\"]\n\n[node name=\"A\" parent=\".\" instance=ExtResource(\"1_b\")]\n";
        string inner = "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n\n[node name=\"B\" type=\"Panel\" parent=\".\"]\n";
        var files = new Files(("res://a.tscn", outer), ("res://b.tscn", inner));

        ConversionResult<TscnDocument> back = Back(OverrideIn("res://a.tscn", "A/B"), files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[2]));
    }

    [Fact]
    public void ANestedRelativeReferenceResolvesAgainstItsOwnFile()
    {
        var files = new Files(("res://ui/a.tscn", Inheriting("b.tscn")), ("res://ui/b.tscn", ControlScene));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://ui/a.tscn", TwoProperties), files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[1]));
        Assert.Equal([(SourceKey, "res://ui/a.tscn"), ("res://ui/a.tscn", "b.tscn")], files.Reads);
    }

    [Fact]
    public void AReferenceReadThroughAnotherKeyUsesThatKeysParser()
    {
        var files = new Files();
        files.Add("res://a.tscn", new SceneFile("res://a.xscn", $"<Control {X} Name=\"Root\" />"));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", TwoProperties), files);

        Assert.Empty(back.Issues);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[1]));
    }

    // Node2D lists no text, so text is a script export and goes after script, as on a Node2D node.
    [Fact]
    public void AScriptExportNamedLikeAPropertyOfAnotherClassStaysAfterScript()
    {
        var files = new Files(("res://a.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node2D\"]\n"));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", "Text='\"Hold\"' Script=\"res://s.gd\""), files);

        Assert.Empty(back.Issues);
        Assert.Equal(["script", "text"], Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void ACycleIsFailedNotAHang()
    {
        var files = new Files(("res://a.tscn", Inheriting("res://b.tscn")), ("res://b.tscn", Inheriting("res://a.tscn")));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", TwoProperties), files);

        AssertWarnedOnce(back, "res://a.tscn", "a cycle", "Inst");
        Assert.Equal(SourceOrder, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void AMissingFileWarnsOnce()
    {
        string document =
            $"<Node {X} Name=\"Root\">"
            + $"<x:Instance Name=\"One\" Scene=\"res://missing.tscn\" {TwoProperties} />"
            + $"<x:Instance Name=\"Two\" Scene=\"res://missing.tscn\" {TwoProperties} />"
            + "</Node>";

        ConversionResult<TscnDocument> back = Back(document, new Files());

        AssertWarnedOnce(back, "res://missing.tscn", "not found", "One");
        Assert.All(back.Document.Nodes.Skip(1), node => Assert.Equal(SourceOrder, Names(node)));
    }

    [Fact]
    public void AnUnparsableFileWarns()
    {
        var files = new Files(("res://a.tscn", BrokenTscn), ("res://b.xscn", "<Control"));
        string document =
            $"<Node {X} Name=\"Root\">"
            + $"<x:Instance Name=\"One\" Scene=\"res://a.tscn\" {TwoProperties} />"
            + $"<x:Instance Name=\"Two\" Scene=\"res://b.xscn\" {TwoProperties} />"
            + "</Node>";

        ConversionResult<TscnDocument> back = Back(document, files);

        Assert.Equal(
            [Warning("res://a.tscn", TscnParseReason(BrokenTscn), "One"), Warning("res://b.xscn", XscnParseReason("<Control"), "Two")],
            back.Issues.Select(issue => (issue.Code, issue.Severity, issue.Message, issue.Location))
        );
        Assert.All(back.Document.Nodes.Skip(1), node => Assert.Equal(SourceOrder, Names(node)));
    }

    [Fact]
    public void AParseErrorGivesItsLine()
    {
        var files = new Files(("res://a.tscn", BrokenTscn));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", TwoProperties), files);

        string reason = TscnParseReason(BrokenTscn);
        Assert.StartsWith("line ", reason, StringComparison.Ordinal);
        AssertWarnedOnce(back, "res://a.tscn", reason, "Inst");
    }

    [Fact]
    public void ANodePathNotFoundNamesThePath()
    {
        var files = new Files(("res://a.tscn", BoxScene));

        ConversionResult<TscnDocument> back = Back(OverrideIn("res://a.tscn", "Nope"), files);

        AssertWarnedOnce(back, "res://a.tscn", "no node at \"Nope\"", "Inst/Nope");
        Assert.Equal(SourceOrder, Names(back.Document.Nodes[2]));
    }

    [Fact]
    public void TwoMissingPathsUnderOneInstanceWarnTwice()
    {
        var files = new Files(("res://a.tscn", BoxScene));
        string document =
            $"<Node {X} Name=\"Root\"><x:Instance Name=\"Inst\" Scene=\"res://a.tscn\">"
            + $"<x:Override Path=\"One\" {TwoProperties} /><x:Override Path=\"Two\" {TwoProperties} />"
            + "</x:Instance></Node>";

        ConversionResult<TscnDocument> back = Back(document, files);

        Assert.Equal(
            [Warning("res://a.tscn", "no node at \"One\"", "Inst/One"), Warning("res://a.tscn", "no node at \"Two\"", "Inst/Two")],
            back.Issues.Select(issue => (issue.Code, issue.Severity, issue.Message, issue.Location))
        );
    }

    [Fact]
    public void ANestedMissingSceneIsNamed()
    {
        var files = new Files(("res://b.tscn", Inheriting("res://missing.tscn")));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://b.tscn", TwoProperties), files);

        AssertWarnedOnce(back, "res://missing.tscn", "not found", "Inst");
        Assert.Equal(SourceOrder, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void AnImportedModelInstanceKeepsSourceOrderSilently()
    {
        var files = new Files(("res://m.glb", ControlScene));
        string document =
            $"<Node {X} Name=\"Root\">"
            + $"<x:Instance Name=\"Model\" Scene=\"res://m.glb\" {TwoProperties} />"
            + $"<x:Instance Name=\"Binary\" Scene=\"res://b.SCN\" {TwoProperties} />"
            + "</Node>";

        ConversionResult<TscnDocument> back = Back(document, files);

        Assert.Empty(back.Issues);
        Assert.All(back.Document.Nodes.Skip(1), node => Assert.Equal(SourceOrder, Names(node)));
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void TheUidIsPassedFromXUids()
    {
        var files = new Files(("res://a.tscn", ControlScene));
        string document =
            $"<Node {X} Name=\"Root\"><x:Uids><x:Uid Path=\"res://a.tscn\" Value=\"uid://abc\" /></x:Uids>"
            + $"<x:Instance Name=\"Inst\" Scene=\"res://a.tscn\" {TwoProperties} /></Node>";

        Back(document, files);

        Assert.Equal(["uid://abc"], files.Uids);
    }

    [Fact]
    public void TheUidIsPassedFromAnExtResource()
    {
        string inheriting =
            "[gd_scene format=3]\n\n[ext_resource type=\"PackedScene\" uid=\"uid://inner\" path=\"res://a.tscn\" id=\"1_s\"]\n\n"
            + "[node name=\"Root\" instance=ExtResource(\"1_s\")]\n";
        var files = new Files(("res://b.tscn", inheriting), ("res://a.tscn", ControlScene));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://b.tscn", TwoProperties), files);

        Assert.Equal([null, "uid://inner"], files.Uids);
        Assert.Equal(LayoutFirst, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void TwoInstancesOfOneSceneReadItOnce()
    {
        var files = new Files(("res://a.tscn", ControlScene));
        string document =
            $"<Node {X} Name=\"Root\">"
            + $"<x:Instance Name=\"One\" Scene=\"res://a.tscn\" {TwoProperties} />"
            + $"<x:Instance Name=\"Two\" Scene=\"res://a.tscn\" {TwoProperties} />"
            + "</Node>";

        ConversionResult<TscnDocument> back = Back(document, files);

        Assert.Single(files.Reads);
        Assert.All(back.Document.Nodes.Skip(1), node => Assert.Equal(LayoutFirst, Names(node)));
    }

    [Fact]
    public void AUidReferenceWarnsWithoutReading()
    {
        var files = new Files(("uid://b1x2y3", ControlScene));

        ConversionResult<TscnDocument> back = Back(InstanceOf("uid://b1x2y3", TwoProperties), files);

        AssertWarnedOnce(back, "uid://b1x2y3", "not found", "Inst");
        Assert.Equal(SourceOrder, Names(back.Document.Nodes[1]));
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void AnUnknownRootClassKeepsSourceOrderSilently()
    {
        var files = new Files(("res://a.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"MyGadget\"]\n"));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", TwoProperties), files);

        Assert.Empty(back.Issues);
        Assert.Equal(SourceOrder, Names(back.Document.Nodes[1]));
    }

    [Fact]
    public void ASectionWithOnePropertyDoesNotRead()
    {
        var files = new Files(("res://a.tscn", ControlScene));

        ConversionResult<TscnDocument> back = Back(InstanceOf("res://a.tscn", "LayoutMode=\"1\""), files);

        Assert.Empty(back.Issues);
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void NoLookupKeepsTodaysBehaviour()
    {
        var files = new Files(("res://a.tscn", ControlScene));
        XscnDocument document = Parse(InstanceOf("res://a.tscn", TwoProperties));

        ConversionResult<TscnDocument> noFiles = XscnToTscn.Convert(document, Metadata, new ConversionOptions { Seed = 7, SourceKey = SourceKey });
        ConversionResult<TscnDocument> noKey = XscnToTscn.Convert(document, Metadata, new ConversionOptions { Seed = 7, SceneFiles = files });

        Assert.Empty(noFiles.Issues);
        Assert.Empty(noKey.Issues);
        Assert.Equal(SourceOrder, Names(noFiles.Document.Nodes[1]));
        Assert.Equal(SourceOrder, Names(noKey.Document.Nodes[1]));
        Assert.Empty(files.Reads);
    }

    private static string InstanceOf(string scene, string properties) =>
        $"<Node {X} Name=\"Root\"><x:Instance Name=\"Inst\" Scene=\"{scene}\" {properties} /></Node>";

    private static string OverrideIn(string scene, string path) =>
        $"<Node {X} Name=\"Root\"><x:Instance Name=\"Inst\" Scene=\"{scene}\"><x:Override Path=\"{path}\" {TwoProperties} /></x:Instance></Node>";

    private static string Inheriting(string scene) =>
        $"[gd_scene format=3]\n\n[ext_resource type=\"PackedScene\" path=\"{scene}\" id=\"1_s\"]\n\n"
        + "[node name=\"Root\" instance=ExtResource(\"1_s\")]\n";

    private static ConversionResult<TscnDocument> Back(string text, Files files) =>
        XscnToTscn.Convert(
            Parse(text),
            Metadata,
            new ConversionOptions
            {
                Seed = 7,
                SceneFiles = files,
                SourceKey = SourceKey,
            }
        );

    private static XscnDocument Parse(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    private static string[] Names(TscnNode node) => [.. node.Properties.Select(property => property.Name)];

    private static (string, ConversionSeverity, string, string) Warning(string reference, string reason, string path) =>
        (
            ConversionCodes.InstancedClassUnreadable,
            ConversionSeverity.Warning,
            $"The class of \"{reference}\" could not be read ({reason}), so this section keeps its written property order.",
            $"node \"{path}\""
        );

    private static void AssertWarnedOnce(ConversionResult<TscnDocument> back, string reference, string reason, string path) =>
        Assert.Equal([Warning(reference, reason, path)], back.Issues.Select(issue => (issue.Code, issue.Severity, issue.Message, issue.Location)));

    private static string TscnParseReason(string text)
    {
        TscnParseException error = Assert.Throws<TscnParseException>(() => TscnReader.Read(text));
        return $"line {error.Line}, column {error.Column}: {error.Reason}";
    }

    private static string XscnParseReason(string text)
    {
        XscnDiagnostic first = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata).Diagnostics.First(d => d.Severity == XscnSeverity.Error);
        LinePosition position = new LineMap(text).OneBased(first.Span.Start);
        return $"line {position.Line}, column {position.Column}: {first.Message}";
    }

    // Scenes by the path a reference resolves to: res:// as written, a relative one against the referrer's folder.
    private sealed class Files : ISceneFiles
    {
        private readonly Dictionary<string, SceneFile> scenes = new(StringComparer.Ordinal);

        public Files(params (string Path, string Text)[] scenes)
        {
            foreach ((string path, string text) in scenes)
            {
                Add(path, new SceneFile(path, text));
            }
        }

        public List<(string Referrer, string Reference)> Reads { get; } = [];

        public List<string?> Uids { get; } = [];

        public void Add(string path, SceneFile file) => scenes.Add(path, file);

        public SceneFile? Read(string referrer, string reference, string? uid)
        {
            Reads.Add((referrer, reference));
            Uids.Add(uid);
            string path = reference.Contains("://", StringComparison.Ordinal) ? reference : referrer[..(referrer.LastIndexOf('/') + 1)] + reference;
            return scenes.GetValueOrDefault(path);
        }
    }
}
