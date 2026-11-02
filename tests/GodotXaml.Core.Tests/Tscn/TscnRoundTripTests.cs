using System.Text;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;

namespace GodotXaml.Core.Tests.Tscn;

// The files tools/scene-fixtures saves with Godot 4.7.2's text saver, under tests/fixtures/scenes/.
public sealed class TscnRoundTripTests
{
    public static TheoryData<string> Cases =>
        new(
            Directory
                .GetFiles(RepoFiles.PathOf("tests", "fixtures", "scenes"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        );

    [Fact]
    public void FindsTheFixtures() => Assert.True(Cases.Count >= 17, $"only {Cases.Count} fixtures under tests/fixtures/scenes");

    // Godot wrote it, so reading it and writing the model back gives the same bytes.
    [Theory]
    [MemberData(nameof(Cases))]
    public void WritesGodotFileBackByteIdentical(string file)
    {
        byte[] expected = File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "scenes", file));
        TscnDocument document = TscnReader.Read(new UTF8Encoding(false, true).GetString(expected));
        byte[] written = Encoding.UTF8.GetBytes(TscnWriter.Write(document));
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(written));
        Assert.Equal(expected, written);
    }
}
