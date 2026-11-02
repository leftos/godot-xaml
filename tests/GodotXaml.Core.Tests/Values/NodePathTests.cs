using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// A NodePath's text, normalised as Godot parses and prints it (core/string/node_path.cpp:394-472, 170-188). Each row's
// expected text is Godot's own var_to_str(str_to_var(text)), in tests/fixtures/values/loose/node_path_*.out.txt.
public sealed class NodePathTests
{
    [Theory]
    [InlineData("a//b", "a/b")]
    [InlineData("a/", "a")]
    [InlineData("a:", "a")]
    [InlineData("./x", "./x")]
    [InlineData("../a/./b", "../a/./b")]
    [InlineData(":prop", ":prop")]
    [InlineData("/root/a", "/root/a")]
    [InlineData("a:b:c", "a:b:c")]
    [InlineData("a/b:c:d", "a/b:c:d")]
    [InlineData("/", "/")]
    [InlineData(".", ".")]
    [InlineData("..", "..")]
    [InlineData("", "")]
    public void HoldsAndWritesGodotText(string text, string normalised)
    {
        GodotNodePath path = Assert.IsType<GodotNodePath>(GodotValueReader.Read("NodePath(\"" + text + "\")"));
        Assert.Equal(normalised, path.Path);
        Assert.Equal("NodePath(\"" + normalised + "\")", Write(path));
    }

    // Two texts Godot parses to one path are one value, since equality is by the text it prints.
    [Fact]
    public void ComparesByTheTextGodotPrints() => Assert.Equal(new GodotNodePath("a//b"), new GodotNodePath("a/b"));

    private static string Write(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);
}
