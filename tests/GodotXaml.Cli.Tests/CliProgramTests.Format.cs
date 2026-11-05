using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli.Tests;

/// <summary>The <c>format</c> command.</summary>
public sealed partial class CliProgramTests
{
    private const string NonCanonicalScene = "<Control  Name=\"Root\"/>";

    private static readonly Lazy<GodotMetadata> ShippedMetadata = new(() =>
        GodotMetadata.LoadBundled(MetadataChoice.Newest(GodotMetadata.BundledVersions))
    );

    [Fact]
    public void FormatRewritesANonCanonicalFileInPlace()
    {
        string file = Write("root.xscn", NonCanonicalScene);
        string canonical = CanonicalScene(NonCanonicalScene);

        RunResult run = Run("format", file);

        Assert.NotEqual(NonCanonicalScene, canonical);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"formatted {file}", "1 of 1 file(s) changed"], run.Lines);
        Assert.Equal("", run.Error);
        Assert.Equal(canonical, File.ReadAllText(file));
    }

    [Fact]
    public void FormatLeavesACanonicalFileUnchanged()
    {
        string file = Write("root.xscn", CanonicalScene(NonCanonicalScene));
        byte[] before = File.ReadAllBytes(file);

        RunResult run = Run("format", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["0 of 1 file(s) changed"], run.Lines);
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void FormatCheckNamesAFileThatWouldChangeAndExitsOne()
    {
        string file = Write("root.xscn", NonCanonicalScene);

        RunResult run = Run("format", "--check", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal([$"{file}: not canonical", "1 of 1 file(s) would change"], run.Lines);
        Assert.Equal(NonCanonicalScene, File.ReadAllText(file));
    }

    [Fact]
    public void FormatCheckOnCanonicalFilesExitsZero()
    {
        string file = Write("root.xscn", CanonicalScene(NonCanonicalScene));

        RunResult run = Run("format", "--check", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["0 of 1 file(s) would change"], run.Lines);
    }

    [Fact]
    public void FormatDoesNotRewriteAFileWithAnError()
    {
        const string Broken = "<Control Name=\"Root\">\n  <Label />\n</Control>";
        string file = Write("error.xscn", Broken);

        RunResult run = Run("format", file);

        string[] expected = [$"{file}(2,4): error GX0104: <Label> has no Name; every node needs one.", "0 of 1 file(s) changed"];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal(Broken, File.ReadAllText(file));
    }

    [Fact]
    public void FormatSearchesAFolderRecursively()
    {
        string canonical = CanonicalScene(NonCanonicalScene);
        Write("a.xscn", canonical);
        string nested = Write("sub/b.xscn", NonCanonicalScene);
        Write("notes.txt", "hello");

        RunResult run = Run("format", folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"formatted {nested}", "1 of 2 file(s) changed"], run.Lines);
        Assert.Equal(canonical, File.ReadAllText(nested));
    }

    [Fact]
    public void FormatRewritesAFileWithOnlyWarnings()
    {
        const string Source = "<Control  Name=\"Root\">\n  <Gadget Name=\"G\" />\n</Control>";
        string file = Write("warning.xscn", Source);
        string canonical = CanonicalScene(Source);

        RunResult run = Run("format", file);

        string[] expected = [$"{file}(2,4): warning GX0107: {UnknownClass}", $"formatted {file}", "1 of 1 file(s) changed"];
        Assert.NotEqual(Source, canonical);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal(canonical, File.ReadAllText(file));
    }

    [Fact]
    public void FormatRefusesATscn()
    {
        string file = Write("scene.tscn", "[gd_scene format=3]\n");
        string[] expected = [$"{file}: only .xscn and .xres files can be formatted."];

        RunResult run = Run("format", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    private static string CanonicalScene(string text)
    {
        GodotMetadata metadata = ShippedMetadata.Value;
        return XscnWriter.Write(XscnParser.Parse(text, XscnDocumentKind.Scene, metadata).Document, metadata);
    }
}
