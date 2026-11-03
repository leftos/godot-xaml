using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;

namespace GodotXaml.Core.Tests.Docs;

public sealed class DocExamplesTests
{
    private static readonly string[] DocumentFolders = ["docs/format", "docs/guide", "agents/skill/godot-xaml"];
    private static readonly string[] DocumentFiles = ["godot/addons/godot_xaml/AGENTS.md", "README.md"];
    private static readonly GodotMetadata Metadata = GodotMetadata.LoadBundled("4.7");
    private static readonly Dictionary<string, DocBlock> Blocks = LoadBlocks();

    public static TheoryData<string> Examples => new(Blocks.Keys.Order(StringComparer.Ordinal));

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Examples))]
    public void AnExampleInTheDocsHolds(string where)
    {
        DocBlock block = Blocks[where];
        string? failure = DocExampleCheck.Check(block, Metadata);
        Assert.True(failure is null, failure ?? string.Empty);
    }

    [Fact]
    public void FormatReferenceHoldsExamples()
    {
        string[] references = ["docs/format/README.md", "docs/format/values.md"];
        foreach (string reference in references)
        {
            string full = RepoFiles.PathOf(reference.Split('/'));
            Assert.True(File.Exists(full), $"{reference} does not exist");
            Assert.True(DocBlocks.Extract(File.ReadAllText(full), reference).Count > 0, $"{reference} holds no tagged XML example");
        }
    }

    private static Dictionary<string, DocBlock> LoadBlocks()
    {
        Dictionary<string, DocBlock> blocks = new(StringComparer.Ordinal);
        foreach (string file in DocumentedFiles())
        {
            foreach (DocBlock block in DocBlocks.Extract(File.ReadAllText(RepoFiles.PathOf(file.Split('/'))), file))
            {
                blocks[$"{block.Path}:{block.Line}"] = block;
            }
        }

        return blocks;
    }

    private static List<string> DocumentedFiles()
    {
        List<string> files = [];
        foreach (string folder in DocumentFolders)
        {
            string full = RepoFiles.PathOf(folder.Split('/'));
            if (!Directory.Exists(full))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories).Select(Relative));
        }

        files.AddRange(DocumentFiles.Where(file => File.Exists(RepoFiles.PathOf(file.Split('/')))));
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static string Relative(string fullPath) => Path.GetRelativePath(RepoFiles.Root, fullPath).Replace('\\', '/');
}
