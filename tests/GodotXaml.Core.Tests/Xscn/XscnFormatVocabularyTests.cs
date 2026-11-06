using System.Text.RegularExpressions;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed partial class XscnFormatVocabularyTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    // Where each x: element is read, as a scene with the element in its place.
    private static readonly Dictionary<string, string> ElementPlaces = new(StringComparer.Ordinal)
    {
        ["Uids"] = $"<Control {X} Name=\"R\"><x:Uids /></Control>",
        ["Uid"] = $"<Control {X} Name=\"R\"><x:Uids><x:Uid Path=\"res://a.xscn\" Value=\"uid://a\" /></x:Uids></Control>",
        ["Resources"] = $"<Control {X} Name=\"R\"><x:Resources /></Control>",
        ["Scene"] = $"<Control {X} Name=\"R\"><x:Scene /></Control>",
        ["Instance"] = $"<Control {X} Name=\"R\"><x:Instance Name=\"I\" Scene=\"res://a.xscn\" /></Control>",
        ["Override"] = $"<Control {X} Name=\"R\"><x:Override Path=\"A\" /></Control>",
        ["Connect"] = $"<Control {X} Name=\"R\"><x:Connect Signal=\"Ready\" Method=\"M\" /></Control>",
        ["Prop"] = $"<Control {X} Name=\"R\"><x:Prop Name=\"a\" Value=\"1\" /></Control>",
    };

    // Each element kind as a scene whose {0} is that element's attributes.
    public static TheoryData<string, string> AttributePlaces =>
        new()
        {
            { "Node", $"<Control {X} Name=\"R\"><Button {{0}} /></Control>" },
            { "Resource", $"<Control {X} Name=\"R\"><Control.Theme><Theme {{0}} /></Control.Theme></Control>" },
            { "TableEntry", $"<Control {X} Name=\"R\"><x:Resources><Theme {{0}} /></x:Resources></Control>" },
            { "Uids", $"<Control {X} Name=\"R\"><x:Uids {{0}} /></Control>" },
            { "Uid", $"<Control {X} Name=\"R\"><x:Uids><x:Uid {{0}} /></x:Uids></Control>" },
            { "Resources", $"<Control {X} Name=\"R\"><x:Resources {{0}} /></Control>" },
            { "Scene", $"<Control {X} Name=\"R\"><x:Scene {{0}} /></Control>" },
            { "Instance", $"<Control {X} Name=\"R\"><x:Instance {{0}} /></Control>" },
            { "Override", $"<Control {X} Name=\"R\"><x:Override {{0}} /></Control>" },
            { "Connect", $"<Control {X} Name=\"R\"><x:Connect {{0}} /></Control>" },
            { "Prop", $"<Control {X} Name=\"R\"><x:Prop {{0}} /></Control>" },
        };

    [Fact]
    public void EveryElementIsReadAsAFormatElement()
    {
        Assert.Equal(ElementPlaces.Keys.Order(StringComparer.Ordinal), XscnFormatVocabulary.Elements.Order(StringComparer.Ordinal));
        foreach (string element in XscnFormatVocabulary.Elements)
        {
            XscnParseResult result = XscnParser.Parse(ElementPlaces[element], XscnDocumentKind.Scene, Real);

            Assert.DoesNotContain(
                result.Diagnostics,
                d => d.Code is XscnCodes.UnknownFormatElement or XscnCodes.UnknownFormatAttribute or XscnCodes.XmlSyntax
            );
        }
    }

    [Theory]
    [MemberData(nameof(AttributePlaces))]
    public void EveryAttributeIsAcceptedOnItsKind(string kind, string place)
    {
        string attributes = string.Join(" ", XscnFormatVocabulary.AttributesFor(kind).Select(a => $"{a}=\"1\""));

        XscnParseResult result = XscnParser.Parse(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, place, attributes),
            XscnDocumentKind.Scene,
            Real
        );

        Assert.DoesNotContain(result.Diagnostics, d => d.Code is XscnCodes.UnknownFormatAttribute or XscnCodes.XmlSyntax);
    }

    [Fact]
    public void TheReservedAttributesSitOnlyWhereTheReaderTakesThem()
    {
        Assert.Contains("x:Key", XscnFormatVocabulary.AttributesFor("TableEntry"));
        Assert.DoesNotContain("x:Key", XscnFormatVocabulary.AttributesFor("Resource"));
        Assert.DoesNotContain("x:Key", XscnFormatVocabulary.AttributesFor("Node"));
        Assert.Contains("Path", XscnFormatVocabulary.AttributesFor("Override"));
        Assert.DoesNotContain("Path", XscnFormatVocabulary.AttributesFor("Node"));
        Assert.Contains("Scene", XscnFormatVocabulary.AttributesFor("Instance"));
        Assert.DoesNotContain("Scene", XscnFormatVocabulary.AttributesFor("Node"));
    }

    [Fact]
    public void AnUnknownKindThrows() => Assert.Throws<ArgumentException>(() => XscnFormatVocabulary.AttributesFor("Bogus"));

    [Fact]
    public void TheFormatReferenceNamesTheSameElements()
    {
        string readme = File.ReadAllText(RepoFiles.PathOf("docs", "format", "README.md"));

        IEnumerable<string> documented = ElementTag().Matches(readme).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(documented, XscnFormatVocabulary.Elements.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheFormatReferenceTableNamesTheSameNodeAttributes()
    {
        string readme = File.ReadAllText(RepoFiles.PathOf("docs", "format", "README.md"));
        string section = readme[readme.IndexOf("## `x:` attributes on a node", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];

        IEnumerable<string> documented = TableAttribute().Matches(section).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);

        Assert.Equal(
            documented,
            XscnFormatVocabulary.AttributesFor("Node").Where(a => a.StartsWith("x:", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
        );
    }

    [GeneratedRegex(@"<x:([A-Za-z]+)")]
    private static partial Regex ElementTag();

    [GeneratedRegex(@"^\| `(x:[A-Za-z]+)`", RegexOptions.Multiline)]
    private static partial Regex TableAttribute();
}
