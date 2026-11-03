using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnPropertyNamesTests
{
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));
    private static readonly XscnPropertyNames Names = new(Real);

    [Fact]
    public void AKnownClassFindsAnInheritedPropertyByItsCSharpName()
    {
        XscnResolvedProperty resolved = Names.Resolve(Class("Label"), "CustomMinimumSize");

        Assert.Equal("custom_minimum_size", resolved.GodotName);
        Assert.Equal("Vector2", resolved.Type.VariantType);
        Assert.Equal("CustomMinimumSize", Names.AttributeName(Class("Label"), "custom_minimum_size"));
    }

    [Fact]
    public void AThemeOverrideIsADottedAttributeTypedByItsFamily()
    {
        XscnResolvedProperty onClass = Names.Resolve(Class("BoxContainer"), "ThemeOverrideConstants.separation");
        XscnResolvedProperty onUnknown = Names.Resolve(null, "ThemeOverrideColors.font_color");

        Assert.Equal("theme_override_constants/separation", onClass.GodotName);
        Assert.Equal("int", onClass.Type.VariantType);
        Assert.Equal("theme_override_colors/font_color", onUnknown.GodotName);
        Assert.Equal("Color", onUnknown.Type.VariantType);
        Assert.Equal("ThemeOverrideConstants.separation", Names.AttributeName(Class("BoxContainer"), "theme_override_constants/separation"));
    }

    [Fact]
    public void AMetadataEntryKeepsItsKeyVerbatimAndHasNoType()
    {
        XscnResolvedProperty resolved = Names.Resolve(Class("Control"), "Metadata._tab_index");

        Assert.Equal("metadata/_tab_index", resolved.GodotName);
        Assert.Null(resolved.Type.VariantType);
        Assert.Equal("Metadata._tab_index", Names.AttributeName(Class("Control"), "metadata/_tab_index"));
    }

    [Fact]
    public void AKeyThatIsNotAnXmlNameHasNoAttributeName()
    {
        Assert.Null(Names.AttributeName(Class("Control"), "metadata/my key"));
        Assert.Null(Names.AttributeName(Class("Control"), "metadata/a:b"));
    }

    // The default library's key is empty: "Libraries." would be an XML name, but no property element can carry it.
    [Fact]
    public void AnEmptyKeyHasNoAttributeName() => Assert.Null(Names.AttributeName(Class("AnimationPlayer"), "libraries/"));

    [Fact]
    public void AnUnknownClassTypesANameOnlyWhenEveryClassAgrees()
    {
        XscnResolvedProperty position = Names.Resolve(null, "Position");
        XscnResolvedProperty preset = Names.Resolve(null, "AnchorsPreset");
        XscnResolvedProperty text = Names.Resolve(null, "Text");
        XscnResolvedProperty fillMode = Names.Resolve(null, "FillMode");

        Assert.Equal("position", position.GodotName);
        Assert.Null(position.Type.VariantType);
        Assert.Equal("anchors_preset", preset.GodotName);
        Assert.Equal("int", preset.Type.VariantType);
        Assert.Equal("FullRect", preset.Type.Names!.Write(15));
        Assert.Equal("text", text.GodotName);
        Assert.Equal("String", text.Type.VariantType);
        Assert.Equal("fill_mode", fillMode.GodotName);
        Assert.Null(fillMode.Type.VariantType);
        Assert.Equal("AnchorsPreset", Names.AttributeName(null, "anchors_preset"));
    }

    [Fact]
    public void AScriptPropertyKeepsItsStoredName()
    {
        XscnResolvedProperty onClass = Names.Resolve(Class("Label"), "rowScene");
        XscnResolvedProperty onUnknown = Names.Resolve(null, "rowScene");

        Assert.Equal("rowScene", onClass.GodotName);
        Assert.Null(onClass.Type.VariantType);
        Assert.Equal("rowScene", onUnknown.GodotName);
        Assert.Equal("rowScene", Names.AttributeName(Class("Label"), "rowScene"));
    }

    [Fact]
    public void AScriptPropertyNamedLikeAnEnginePropertyHasNoAttributeName()
    {
        Assert.Null(Names.AttributeName(Class("Label"), "Text"));
        Assert.Equal("Text", Names.AttributeName(Class("Node"), "Text"));
    }

    [Fact]
    public void AStringPropertyWithAStringNameDefaultIsAStringName()
    {
        XscnResolvedProperty resolved = Names.Resolve(Class("Label"), "ThemeTypeVariation");

        Assert.Equal("theme_type_variation", resolved.GodotName);
        Assert.Equal("StringName", resolved.Type.VariantType);
    }

    private static GodotClassInfo Class(string name) => Real.FindClass(name) ?? throw new InvalidOperationException($"No class {name}.");
}
