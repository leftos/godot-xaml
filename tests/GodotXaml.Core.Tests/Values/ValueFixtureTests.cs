using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// The fixture files tools/value-fixtures writes from Godot itself, under tests/fixtures/values/canonical and loose.
public sealed class ValueFixtureTests
{
    private const string ErrorPrefix = "expects_error_";

    // The canonical case Godot itself does not read back as written: c_escape writes \a and \v (ustring.cpp:4477,
    // 4483), and the tokenizer reads them as the letters (variant_parser.cpp:351-353).
    private const string LossyCase = "string_name_bel_vt";

    public static TheoryData<string> CanonicalCases => new(CaseNames("canonical", ".txt").Where(name => name != LossyCase));

    public static TheoryData<string> LooseCases =>
        new(CaseNames("loose", ".in.txt").Where(name => !name.StartsWith(ErrorPrefix, StringComparison.Ordinal)));

    public static TheoryData<string> ErrorCases =>
        new(CaseNames("loose", ".in.txt").Where(name => name.StartsWith(ErrorPrefix, StringComparison.Ordinal)));

    // var_to_str(value): reading and writing it gives the same bytes, and the written text reads as the same value.
    [Theory]
    [MemberData(nameof(CanonicalCases))]
    public void WritesGodotTextBackUnchanged(string name)
    {
        string expected = Fixture("canonical", name + ".txt");
        GodotValue value = GodotValueReader.Read(expected);
        string written = GodotValueWriter.Write(value, ValueWriteOptions.Default);
        Assert.Equal(expected, written);
        Assert.Equal(value, GodotValueReader.Read(written));
    }

    [Fact]
    public void WritesBelAndVtAsGodotDoesAndReadsThemBackAsLetters()
    {
        string expected = Fixture("canonical", LossyCase + ".txt");
        Assert.Equal(expected, GodotValueWriter.Write(new GodotStringName("bel\a vt\v"), ValueWriteOptions.Default));
        Assert.Equal(new GodotStringName("bela vtv"), GodotValueReader.Read(expected));
    }

    // var_to_str(str_to_var(input)): a loose or legacy input is written as Godot writes it, and both read as one value.
    [Theory]
    [MemberData(nameof(LooseCases))]
    public void WritesLooseInputAsGodotDoes(string name)
    {
        GodotValue value = GodotValueReader.Read(Fixture("loose", name + ".in.txt"));
        string expected = Fixture("loose", name + ".out.txt");
        Assert.Equal(expected, GodotValueWriter.Write(value, ValueWriteOptions.Default));
        Assert.Equal(GodotValueReader.Read(expected), value);
    }

    // Inputs str_to_var gives null for.
    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void RejectsWhatGodotFailsToParse(string name) =>
        Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(Fixture("loose", name + ".in.txt")));

    private static string Fixture(string folder, string file) => File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "values", folder, file));

    private static IEnumerable<string> CaseNames(string folder, string suffix) =>
        Directory
            .GetFiles(RepoFiles.PathOf("tests", "fixtures", "values", folder), "*" + suffix)
            .Select(path => Path.GetFileName(path)[..^suffix.Length])
            .Order(StringComparer.Ordinal);
}
