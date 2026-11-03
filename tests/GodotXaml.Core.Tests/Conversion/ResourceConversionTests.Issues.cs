using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// One test per ConversionCodes code: the smallest document that triggers it, and the code and severity it reports.
public sealed partial class ResourceConversionTests
{
    private const string Header = "[gd_resource type=\"Resource\" format=3]\n\n";

    [Fact]
    public void HeaderAttributeWithNoPlaceIsUnrepresentable()
    {
        TscnDocument document = TscnReader.Read(Header + "[resource]\n") with
        {
            Header = new TscnHeader { Type = "Resource", Extra = [new TscnField("load_order", "\"1\"")] },
        };

        AssertIssue(Forward(document), ConversionCodes.UnrepresentableAttribute, ConversionSeverity.Error);
    }

    // TscnReader refuses a reference to an undefined id as Godot's loader does, so these models are built in code.
    [Fact]
    public void ExtResourceNamingNoSectionIsReported() =>
        AssertIssue(
            Forward(WithRootProperty(new GodotResourceRef(GodotRefKind.External, "9_n0n0n"))),
            ConversionCodes.UnknownExtResourceId,
            ConversionSeverity.Error
        );

    [Fact]
    public void SubResourceNamingNoSectionIsReported() =>
        AssertIssue(
            Forward(WithRootProperty(new GodotResourceRef(GodotRefKind.Sub, "Gone_n0n0n"))),
            ConversionCodes.UnknownSubResourceId,
            ConversionSeverity.Error
        );

    [Fact]
    public void UnusedExtResourceIsReported() =>
        AssertIssue(
            Forward(Header + "[ext_resource type=\"Texture2D\" path=\"res://a.png\" id=\"1_aaaaa\"]\n\n[resource]\n"),
            ConversionCodes.UnusedExtResource,
            ConversionSeverity.Warning
        );

    [Fact]
    public void UnusedSubResourceIsReported() =>
        AssertIssue(
            Forward(Header + "[sub_resource type=\"Gradient\" id=\"Gradient_g1111\"]\n\n[resource]\n"),
            ConversionCodes.UnusedSubResource,
            ConversionSeverity.Warning
        );

    [Fact]
    public void SubResourceCycleIsReportedForEachSubResourceOnIt()
    {
        TscnDocument document = TscnReader.Read(
            Header
                + "[sub_resource type=\"Resource\" id=\"Resource_a1111\"]\n\n"
                + "[sub_resource type=\"Resource\" id=\"Resource_b1111\"]\nmetadata/next = SubResource(\"Resource_a1111\")\n\n"
                + "[resource]\nmetadata/start = SubResource(\"Resource_a1111\")\n"
        );
        TscnProperty back = new("metadata/next", new GodotResourceRef(GodotRefKind.Sub, "Resource_b1111"));

        ImmutableArray<ConversionIssue> issues = Forward(
            document with
            {
                SubResources = document.SubResources.SetItem(0, document.SubResources[0] with { Properties = [back] }),
            }
        );

        Assert.Equal(2, issues.Count(issue => issue.Code == ConversionCodes.SubResourceCycle && issue.Severity == ConversionSeverity.Error));
    }

    [Fact]
    public void SecondSectionWithAnIdIsReported()
    {
        TscnDocument document = TscnReader.Read(
            Header + "[sub_resource type=\"Gradient\" id=\"Gradient_g1111\"]\n\n[resource]\nmetadata/a = SubResource(\"Gradient_g1111\")\n"
        );

        AssertIssue(
            Forward(document with { SubResources = document.SubResources.Add(document.SubResources[0]) }),
            ConversionCodes.DuplicateId,
            ConversionSeverity.Error
        );
    }

    [Fact]
    public void ClassTheMetadataLacksIsReported() =>
        AssertIssue(
            Forward(
                Header
                    + "[sub_resource type=\"MyExtensionThing\" id=\"MyExtensionThing_x1111\"]\n\n"
                    + "[resource]\nmetadata/a = SubResource(\"MyExtensionThing_x1111\")\n"
            ),
            ConversionCodes.UnknownClass,
            ConversionSeverity.Warning
        );

    [Fact]
    public void KeyNamingNoEntryIsReportedAndThePropertyDropped()
    {
        ConversionResult<TscnDocument> back = Back(Xres(Res("Resource", new XscnProperty("metadata/a", new XscnKeyRef("Nope")))));

        AssertIssue(back.Issues, ConversionCodes.UnknownKey, ConversionSeverity.Error);
        Assert.Empty(back.Document.ResourceProperties);
    }

    [Fact]
    public void InvalidValueIsReportedAndThePropertyDropped()
    {
        ConversionResult<TscnDocument> back = Back(
            Xres(Res("Resource", new XscnProperty("metadata/a", new XscnInvalid("Vector2(", "Expected a value."))))
        );

        AssertIssue(back.Issues, ConversionCodes.InvalidValue, ConversionSeverity.Error);
        Assert.Empty(back.Document.ResourceProperties);
    }

    [Fact]
    public void ExtResourceInsideXscnTextIsReported() =>
        AssertIssue(
            Back(Xres(Res("Resource", Prop("metadata/a", new GodotArray([new GodotResourceRef(GodotRefKind.External, "1_aaaaa")], null))))).Issues,
            ConversionCodes.ExtResourceInText,
            ConversionSeverity.Error
        );

    [Fact]
    public void UnusedTableEntryIsReported() =>
        AssertIssue(
            Back(Xres(Res("Resource"), Res("Gradient") with { Key = "Spare" })).Issues,
            ConversionCodes.UnusedResourceEntry,
            ConversionSeverity.Warning
        );

    [Fact]
    public void ScriptWithoutAScriptClassLookupIsReported()
    {
        XscnDocument document = Xres(Res("Resource", new XscnProperty("script", new XscnFile("res://tagged.gd"))));

        ConversionResult<TscnDocument> back = Back(document, Options with { ScriptClassOf = null });

        AssertIssue(back.Issues, ConversionCodes.ScriptClassUnknown, ConversionSeverity.Warning);
        Assert.Null(back.Document.Header.ScriptClass);
    }

    [Fact]
    public void ResourceWhoseTagNamesASceneIsReported() =>
        AssertIssue(
            Back(Xres(new XscnResource { Type = new XscnSceneType("res://menu.tscn") })).Issues,
            ConversionCodes.NotAResource,
            ConversionSeverity.Error
        );

    [Fact]
    public void ResourceLoopIsReported()
    {
        XscnResource first = Res("Resource", new XscnProperty("metadata/next", new XscnKeyRef("B"))) with { Key = "A" };
        XscnResource second = Res("Resource", new XscnProperty("metadata/next", new XscnKeyRef("A"))) with { Key = "B" };

        AssertIssue(
            Back(Xres(Res("Resource", new XscnProperty("metadata/start", new XscnKeyRef("A"))), first, second)).Issues,
            ConversionCodes.ResourceCycle,
            ConversionSeverity.Error
        );
    }

    [Fact]
    public void TableEntryWithoutAKeyIsReported() =>
        AssertIssue(Back(Xres(Res("Resource"), Res("Gradient"))).Issues, ConversionCodes.MissingKey, ConversionSeverity.Error);

    [Fact]
    public void TableEntryWithARepeatedKeyIsReported()
    {
        XscnDocument document = Xres(
            Res("Resource", new XscnProperty("metadata/a", new XscnKeyRef("G"))),
            Res("Gradient") with
            {
                Key = "G",
            },
            Res("Curve") with
            {
                Key = "G",
            }
        );

        AssertIssue(Back(document).Issues, ConversionCodes.RepeatedKey, ConversionSeverity.Error);
    }

    [Fact]
    public void UidWithoutAPathIsReported() =>
        AssertIssue(
            Back(Xres(Res("Resource", Prop("metadata/a", new GodotArray([new GodotPathRef("uid://b1x2y3", null)], null))))).Issues,
            ConversionCodes.UidWithoutPath,
            ConversionSeverity.Error
        );

    // A quoted key keeps the character Godot's loader drops from an unquoted one, and XML 1.0 cannot hold it at all, so
    // the property has no attribute or x:Prop name to be written by.
    [Fact]
    public void ResourcePropertyNameHoldingAControlCharacterIsReportedAndDropped()
    {
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(
            TscnReader.Read(Header + "[resource]\n\"metadata/a\\u0001b\" = 1\n"),
            Metadata,
            Options
        );

        ConversionIssue issue = Assert.Single(result.Issues);
        Assert.Equal(
            (
                ConversionCodes.PropertyNameNotXml,
                ConversionSeverity.Error,
                "The property name \"metadata/a\\u0001b\" holds a character XML cannot hold; the property is dropped.",
                "[resource]"
            ),
            (issue.Code, issue.Severity, issue.Message, issue.Location)
        );
        Assert.Empty(RootProperties(result.Document));
        Assert.DoesNotContain("metadata/a", XscnWriter.Write(result.Document, Metadata), StringComparison.Ordinal);
    }

    [Fact]
    public void NodePropertyNameHoldingAControlCharacterIsReportedAndDropped()
    {
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(
            TscnReader.Read("[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n\"metadata/a\\u0001b\" = 1\n"),
            Metadata,
            Options
        );

        ConversionIssue issue = Assert.Single(result.Issues);
        Assert.Equal(
            (ConversionCodes.PropertyNameNotXml, ConversionSeverity.Error, "[node name=\"Root\"]"),
            (issue.Code, issue.Severity, issue.Location)
        );
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Properties);
        Assert.DoesNotContain("metadata/a", XscnWriter.Write(result.Document, Metadata), StringComparison.Ordinal);
    }

    private static ImmutableArray<ConversionIssue> Forward(string text) => Forward(TscnReader.Read(text));

    private static TscnDocument WithRootProperty(GodotValue value) =>
        TscnReader.Read(Header + "[resource]\n") with
        {
            ResourceProperties = [new TscnProperty("metadata/a", value)],
        };

    private static ImmutableArray<ConversionIssue> Forward(TscnDocument document) => TscnToXscn.Convert(document, Metadata, Options).Issues;

    private static ConversionResult<TscnDocument> Back(XscnDocument document) => Back(document, Options);

    private static ConversionResult<TscnDocument> Back(XscnDocument document, ConversionOptions options) =>
        XscnToTscn.Convert(document, Metadata, options);

    // The issues hold one with the code, and it has the severity.
    private static void AssertIssue(ImmutableArray<ConversionIssue> issues, string code, ConversionSeverity severity) =>
        Assert.Equal(severity, Assert.Single(issues, issue => issue.Code == code).Severity);
}
