using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnEnumTextTests
{
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void AnEnumPropertyUsesItsMembersCSharpName()
    {
        XscnEnumNames names = NamesOf("Control", "mouse_filter");

        Assert.False(names.IsFlags);
        Assert.Equal("Ignore", names.Write(2));
        Assert.Equal(2, names.Read("Ignore"));
        Assert.Equal(2, names.Read("2"));
        Assert.Null(names.Read("ignore"));
        Assert.Null(names.Read("Nope"));
    }

    [Theory]
    [InlineData(3, "ExpandFill")]
    [InlineData(5, "Fill, ShrinkCenter")]
    [InlineData(0, "ShrinkBegin")]
    [InlineData(16, "16")]
    [InlineData(17, "17")]
    public void ABitfieldPropertyIsWrittenAsDotNetFlagsText(long value, string text)
    {
        XscnEnumNames names = NamesOf("Control", "size_flags_horizontal");

        Assert.True(names.IsFlags);
        Assert.Equal(text, names.Write(value));
        Assert.Equal(value, names.Read(text));
    }

    [Fact]
    public void ABitfieldReadsNamesInAnyOrderAndRejectsAnUnknownOne()
    {
        XscnEnumNames names = NamesOf("Control", "size_flags_horizontal");

        Assert.Equal(5, names.Read("ShrinkCenter,Fill"));
        Assert.Equal(5, names.Read("Fill, 4"));
        Assert.Null(names.Read("Fill, Bogus"));
        Assert.Null(names.Read("Fill,,Expand"));
        Assert.Null(names.Read(""));
    }

    [Theory]
    [InlineData(15, "FullRect")]
    [InlineData(-1, "Custom")]
    [InlineData(13, "VCenterWide")]
    [InlineData(0, "TopLeft")]
    [InlineData(99, "99")]
    public void AHintOnlyIntUsesTheHintsExplicitValues(long value, string text)
    {
        XscnEnumNames names = NamesOf("Control", "anchors_preset");

        Assert.Equal(text, names.Write(value));
        Assert.Equal(value, names.Read(text));
    }

    [Fact]
    public void AHintOnlyIntWithoutValuesCountsFromZero()
    {
        XscnEnumNames names = NamesOf("Control", "layout_mode");

        Assert.Equal(1, names.Read("Anchors"));
        Assert.Equal(1, names.Read("1"));
        Assert.Equal("Anchors", names.Write(1));
        Assert.Equal("Uncontrolled", names.Write(3));
    }

    [Fact]
    public void AnEmptyHintMeansTheInteger()
    {
        XscnEnumNames names = NamesOf("AudioStreamInteractive", "initial_clip");

        Assert.Empty(names.Names);
        Assert.Equal("3", names.Write(3));
        Assert.Equal(3, names.Read("3"));
        Assert.Null(names.Read("Clip"));
    }

    [Fact]
    public void AHintDisplayNameIsPascalCasedOnEveryNonLetterOrDigit()
    {
        XscnEnumNames fill = NamesOf("TextureProgressBar", "fill_mode");
        XscnEnumNames execution = NamesOf("SkeletonModification2D", "execution_mode");

        Assert.Equal("BilinearLeftAndRight", fill.Write(6));
        Assert.Equal(6, fill.Read("BilinearLeftAndRight"));
        Assert.Equal("PhysicsProcess", execution.Write(1));
    }

    [Fact]
    public void AFlagsHintWithoutAnEnumTakesOneBitPerName()
    {
        XscnEnumNames names = NamesOf("Tree", "drop_mode_flags");

        Assert.True(names.IsFlags);
        Assert.Equal("OnItem, InBetween", names.Write(3));
        Assert.Equal("InBetween", names.Write(2));
        Assert.Equal("0", names.Write(0));
        Assert.Equal(3, names.Read("InBetween, OnItem"));
    }

    [Fact]
    public void APropertyWithoutAnEnumOrHintHasNoNames()
    {
        Assert.Null(XscnEnumText.For(Real, Property("Control", "custom_minimum_size")));
        Assert.Null(XscnEnumText.For(Real, Property("Control", "z_index")));
    }

    [Fact]
    public void AHintNameOfOnlyDigitsIsNoNameAndItsIntegerIsUsed()
    {
        XscnEnumNames names = HintNames(HintEnum, "8,16,32");

        Assert.Empty(names.Names);
        Assert.Equal("1", names.Write(1));
        Assert.Equal(16, names.Read("16"));
    }

    [Fact]
    public void ASecondHintNameWithTheSamePascalCaseFormIsNoName()
    {
        XscnEnumNames names = HintNames(HintEnum, "A-B,A B");

        Assert.Equal("AB", names.Write(0));
        Assert.Equal("1", names.Write(1));
        Assert.Equal(0, names.Read("AB"));
    }

    [Fact]
    public void AHintValueIsReadAsGodotsToIntReadsIt()
    {
        XscnEnumNames names = HintNames(HintEnum, "Big:1_024,Half:4.0,Neg:-5");

        Assert.Equal(1024, names.Read("Big"));
        Assert.Equal(4, names.Read("Half"));
        Assert.Equal(-5, names.Read("Neg"));
    }

    [Fact]
    public void AFlagsHintSkipsAnExplicitZero()
    {
        XscnEnumNames names = HintNames(HintFlags, "None:0,A,B");

        Assert.DoesNotContain(names.Names, n => n.Name == "None");
        Assert.Equal("0", names.Write(0));
        Assert.Equal("A", names.Write(2));
        Assert.Equal("B", names.Write(4));
    }

    [Fact]
    public void AFlagsHintValueIsAnUnsigned32BitInteger()
    {
        XscnEnumNames names = HintNames(HintFlags, "A,All:-1");

        Assert.Equal(4294967295, names.Read("All"));
        Assert.Equal("All", names.Write(4294967295));
    }

    [Fact]
    public void AFlagsHintsThirtyThirdOptionWrapsToTheFirstBit()
    {
        XscnEnumNames names = HintNames(HintFlags, string.Join(",", Enumerable.Range(0, 33).Select(i => "F" + i)));

        Assert.Equal(1, names.Read("F32"));
        Assert.Equal("F0", names.Write(1));
    }

    private const int HintEnum = 2;
    private const int HintFlags = 6;

    private static XscnEnumNames HintNames(int hint, string hintString) =>
        XscnEnumText.For(Real, new GodotPropertyInfo("p", "P", "int", null, false, hint, hintString, 6, "", null, null))!;

    private static XscnEnumNames NamesOf(string className, string propertyName) =>
        XscnEnumText.For(Real, Property(className, propertyName)) ?? throw new InvalidOperationException($"{className}.{propertyName} has no names.");

    private static GodotPropertyInfo Property(string className, string propertyName) =>
        Real.FindProperty(Real.FindClass(className)!, propertyName)
        ?? throw new InvalidOperationException($"No property {className}.{propertyName}.");
}
