using System.Text;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Metadata;

public sealed class GodotMetadataTests
{
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void LoadsTheClassesOfTheRealFile()
    {
        GodotClassInfo label = Class("Label");

        Assert.Equal("Label", label.CSharpName);
        Assert.Equal("Control", label.Parent);
        Assert.True(label.Instantiable);
        Assert.Same(Class("Object"), Real.FindClassByCSharpName("GodotObject"));
        Assert.Same(Class("CPUParticles2D"), Real.FindClassByCSharpName("CpuParticles2D"));
        Assert.Null(Real.FindClass("NoSuchClass"));
    }

    [Fact]
    public void WalksButtonsParentChainToObject()
    {
        var chain = new List<string>();
        for (GodotClassInfo? cls = Class("Button"); cls is not null; cls = cls.Parent is null ? null : Real.FindClass(cls.Parent))
        {
            chain.Add(cls.Name);
        }

        Assert.Equal(["Button", "BaseButton", "Control", "CanvasItem", "Node", "Object"], chain);
        Assert.True(Real.Inherits(Class("Button"), "Object"));
        Assert.True(Real.Inherits(Class("Button"), "Button"));
        Assert.False(Real.Inherits(Class("Button"), "Node2D"));
    }

    [Fact]
    public void FindsAnInheritedPropertyByItsCSharpName()
    {
        GodotPropertyInfo? found = Real.FindPropertyByCSharpName(Class("Label"), "CustomMinimumSize");

        Assert.NotNull(found);
        Assert.Contains(found, Class("Control").Properties);
        Assert.Same(found, Real.FindProperty(Class("Label"), "custom_minimum_size"));
        Assert.Null(Real.FindPropertyByCSharpName(Class("Label"), "custom_minimum_size"));
        Assert.Null(Real.FindProperty(Class("Control"), "text"));
    }

    [Fact]
    public void ReadsAPropertysDefaultAndEnumType()
    {
        GodotPropertyInfo mouseFilter = Real.FindProperty(Class("Control"), "mouse_filter")!;

        Assert.Equal(new GodotInt(0), mouseFilter.Default);
        Assert.Equal("0", mouseFilter.DefaultText);
        Assert.Equal("Control.MouseFilter", mouseFilter.EnumType);
        Assert.Equal("MouseFilter", mouseFilter.CSharpName);
        Assert.True(mouseFilter.IsStored);
    }

    [Fact]
    public void FindsAThemeOverride()
    {
        GodotThemeOverrideInfo? fontColor = Real.FindThemeOverride(Class("Label"), "theme_override_colors/font_color");

        Assert.NotNull(fontColor);
        Assert.Equal("Color", fontColor.Type);
        Assert.Null(Real.FindThemeOverride(Class("Node2D"), "theme_override_colors/font_color"));
    }

    [Fact]
    public void FindsClassAndGlobalEnums()
    {
        GodotEnumInfo? mouseFilter = Real.FindEnum("Control.MouseFilter");

        Assert.NotNull(mouseFilter);
        Assert.Equal(["Stop", "Pass", "Ignore"], mouseFilter.Members.Select(m => m.CSharpName));
        Assert.Equal([0L, 1L, 2L], mouseFilter.Members.Select(m => m.Value));
        Assert.Same(mouseFilter, Real.FindEnum("Label.MouseFilter"));
        Assert.Equal("HorizontalAlignment", Real.FindEnum("HorizontalAlignment")?.QualifiedName);
        Assert.Equal("Variant.Type", Real.FindEnum("Variant.Type")?.QualifiedName);
        Assert.True(Real.FindEnum("Control.SizeFlags")?.Bitfield);
        Assert.Null(Real.FindEnum("Vector3.Axis"));
        Assert.Null(Real.FindEnum("Control.NoSuchEnum"));
    }

    [Fact]
    public void AppendsAnUnderscoreToAPropertyNamedLikeItsClass()
    {
        GodotMetadata metadata = LoadJson(ObjectClass, Class("Foo", "Object", Property("foo")));

        Assert.Equal("Foo_", metadata.FindProperty(metadata.FindClass("Foo")!, "foo")?.CSharpName);
    }

    [Fact]
    public void RejectsAClassWithoutSignals()
    {
        const string withoutSignals = """ "Object": {"parent": null, "instantiable": true, "enums": {}, "properties": []} """;
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => LoadJson(withoutSignals));

        Assert.Contains("'Object'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'signals'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadsTheSignalsOfTheRealFile()
    {
        GodotSignalInfo bodyEntered = Assert.Single(Class("Area2D").Signals, s => s.Name == "body_entered");

        Assert.Equal("BodyEntered", bodyEntered.CSharpName);
        Assert.Equal(new GodotSignalArgument("body", "Object", "Node2D"), Assert.Single(bodyEntered.Arguments));
        Assert.Null(Assert.Single(Class("Control").Signals, s => s.Name == "_layout_pending_finished").CSharpName);
    }

    [Fact]
    public void AppendsSignalToASignalNamedLikeAPropertyOfItsClass()
    {
        string withClash = $$"""
             "Object": {"parent": null, "instantiable": true, "enums": {}, "properties": [{{Property("toggled")}}],
               "signals": [{"name": "toggled", "args": []}, {"name": "pressed", "args": []}]}
            """;
        GodotClassInfo cls = LoadJson(withClash).FindClass("Object")!;

        Assert.Equal(["ToggledSignal", "Pressed"], cls.Signals.Select(s => s.CSharpName));
        Assert.Equal("Toggled", cls.Properties[0].CSharpName);
    }

    [Theory]
    [InlineData("\"0\"")]
    [InlineData("null")]
    public void RejectsAnEnumValueThatIsNotAnInteger(string value)
    {
        string withEnum = $$"""
             "Object": {"parent": null, "instantiable": true, "properties": [], "signals": [],
               "enums": {"Mode": {"bitfield": false, "values": [["MODE_A", {{value}}]] } } }
            """;
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => LoadJson(withEnum));

        Assert.Contains("Object.Mode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAClassFromMetadataThatDoesNotHaveIt()
    {
        GodotMetadata other = LoadJson(ObjectClass, Class("Foo", "Object"));

        Assert.Throws<ArgumentException>(() => Real.FindProperty(other.FindClass("Foo")!, "text"));
        Assert.Throws<ArgumentException>(() => Real.FindPropertyByCSharpName(other.FindClass("Foo")!, "Text"));
        Assert.Throws<ArgumentException>(() => Real.Inherits(other.FindClass("Foo")!, "Object"));
        Assert.Throws<ArgumentException>(() => Real.FindThemeOverride(other.FindClass("Foo")!, "theme_override_colors/font_color"));
    }

    [Fact]
    public void ResolvesAClassFromOtherMetadataByItsName()
    {
        GodotMetadata other = LoadJson(ObjectClass, Class("Label", "Object"));

        Assert.True(Real.Inherits(other.FindClass("Label")!, "Control"));
        Assert.Same(Real.FindProperty(Class("Control"), "mouse_filter"), Real.FindProperty(other.FindClass("Label")!, "mouse_filter"));
        Assert.NotNull(Real.FindThemeOverride(other.FindClass("Label")!, "theme_override_colors/font_color"));
    }

    [Fact]
    public void LoadsSignalsAndIgnoresUnknownKeys()
    {
        const string withSignals = """
            "Object": {"parent": null, "instantiable": true, "enums": {}, "properties": [], "future": 1,
              "signals": [{"name": "button_down", "args": [{"name": "toggled_on", "type": "bool", "className": ""}]}]}
            """;
        GodotSignalInfo signal = Assert.Single(LoadJson(withSignals).FindClass("Object")!.Signals);

        Assert.Equal("ButtonDown", signal.CSharpName);
        Assert.Equal(new GodotSignalArgument("toggled_on", "bool", ""), Assert.Single(signal.Arguments));
    }

    [Fact]
    public void RejectsAParentNamingNoClass()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => LoadJson(ObjectClass, Class("Foo", "Missing")));

        Assert.Contains("'Foo'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAFileWithoutClasses()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Load("""{"godot": "4.7", "globalEnums": {}}"""));

        Assert.Contains("'classes'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsADefaultThatDoesNotParse()
    {
        string property = Property("size").Replace("\"usage\"", "\"default\": \"Vector2(\", \"usage\"", StringComparison.Ordinal);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => LoadJson(ObjectClass, Class("Foo", "Object", property)));

        Assert.Contains("Foo.size", error.Message, StringComparison.Ordinal);
    }

    private const string ObjectClass = """ "Object": {"parent": null, "instantiable": true, "enums": {}, "properties": [], "signals": []} """;

    private static GodotClassInfo Class(string name) => Real.FindClass(name) ?? throw new InvalidOperationException($"No class {name}.");

    private static string Class(string name, string parent, params string[] properties) =>
        $$"""
             "{{name}}": {"parent": "{{parent}}", "instantiable": true, "enums": {},
               "properties": [{{string.Join(", ", properties)}}], "signals": []}
            """;

    private static string Property(string name) =>
        $$"""
            {"name": "{{name}}", "type": "int", "enumType": null, "bitfield": false, "hint": 0, "hintString": "", "className": "", "usage": 6}
            """;

    private static GodotMetadata LoadJson(params string[] classes) =>
        Load($$$"""{"godot": "4.7", "globalEnums": {}, "classes": { {{{string.Join(", ", classes)}}} }}""");

    private static GodotMetadata Load(string json) => GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
}
