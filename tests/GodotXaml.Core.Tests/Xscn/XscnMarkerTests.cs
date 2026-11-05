using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnMarkerTests
{
    private const string X = "xmlns:x=\"" + XscnParser.FormatNamespace + "\"";
    private const string G = "xmlns:g=\"" + XscnParser.FormatNamespace + "\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    public static TheoryData<string, string?> MarkerCases =>
        new()
        {
            { $"""<Control {X} x:Godot="4.7" Name="Root" />""", "4.7" },
            { $"""<Control {G} g:Godot="4.8" Name="Root" />""", "4.8" },
            { $"""<Control xmlns:x="res://ui/" x:Godot="4.7" Name="Root" />""", null },
            { $"""<Control {X} {G} g:Godot="4.8" Name="Root" />""", null },
            { $"""<Control {X} Name="Root"><Pivot x:Godot="4.7" Name="Child" /></Control>""", null },
            { $"""<Control {X} Name="Root" />""", null },
            { $"""<Control {X} x:Godot="4" Name="Root" />""", null },
            { $"""<Control {X} x:Godot="four.seven" Name="Root" />""", null },
            { $"""<Control {X} x:Godot="4&#46;10" Name="Root" />""", "4.10" },
            { $"""<!-- a comment --><Control {X} x:Godot="4.7" Name="Root" />""", "4.7" },
            { "", null },
            { "<", null },
            { $"""<StyleBoxFlat {X} x:Godot="4.7"><ContentMarginLeft>4.0</ContentMarginLeft></StyleBoxFlat>""", "4.7" },
        };

    public static TheoryData<string> Documents =>
        new(
            Directory
                .GetFiles(RepoFiles.PathOf("tests", "fixtures", "converted"))
                .Concat(Directory.GetFiles(RepoFiles.PathOf("tests", "fixtures", "xscn", "canonical")))
                .Where(p => p.EndsWith(".xscn", StringComparison.Ordinal) || p.EndsWith(".xres", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
        );

    [Theory]
    [MemberData(nameof(MarkerCases))]
    public void TheMarkerIsReadFromTheRoot(string text, string? expected) => Assert.Equal(expected, XscnMarker.Read(text));

    [Theory]
    [MemberData(nameof(Documents))]
    public void ReadAgreesWithTheParse(string path)
    {
        string text = File.ReadAllText(path);
        XscnDocumentKind kind = path.EndsWith(".xres", StringComparison.Ordinal) ? XscnDocumentKind.Resource : XscnDocumentKind.Scene;

        Assert.Equal(XscnParser.Parse(text, kind, Real).Document.Godot, XscnMarker.Read(text));
    }

    [Fact]
    public void TheMessagesNameBothVersions()
    {
        Assert.Equal("written with Godot 4.8's metadata, which this tool does not carry; reading it with 4.7's", XscnMarker.NotCarried("4.8", "4.7"));
        Assert.Equal(
            "written with Godot 4.8's metadata, which this tool does not carry (it has 4.7's); "
                + "not written, so the file keeps 4.8's typing and marker",
            XscnMarker.NotWritten("4.8", "4.7")
        );
    }
}
