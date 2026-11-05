using System.Text;

namespace GodotXaml.Cli.Tests;

/// <summary>Runs the tool in process, against files written to a temporary folder of its own.</summary>
public sealed partial class CliProgramTests : IDisposable
{
    private const string UnknownClass = "No Godot class is named Gadget; the element is kept with an unknown type.";

    // Each test's own folder, holding its project and everything the run writes beside it, so the backup never outlives
    // the test either.
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "godot-xaml-cli-tests", Guid.NewGuid().ToString("N"));
    private readonly string folder;

    /// <summary>Creates the test's own sandbox folder and the project folder in it.</summary>
    public CliProgramTests()
    {
        folder = Path.Combine(sandbox, "project");
        Directory.CreateDirectory(folder);
    }

    /// <summary>Removes the test's sandbox folder and everything in it.</summary>
    public void Dispose()
    {
        if (Directory.Exists(sandbox))
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void ACleanSceneChecksWithTheSummaryAlone()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");

        RunResult run = Run("check", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void AnErrorPrintsItsCanonicalLineAndExitsOne()
    {
        string file = Write("error.xscn", "<Control Name=\"Root\">\n  <Label />\n</Control>");

        RunResult run = Run("check", file);

        string[] expected = [$"{file}(2,4): error GX0104: <Label> has no Name; every node needs one.", "1 file(s) checked, 1 error(s), 0 warning(s)"];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void AWarningAloneExitsZero()
    {
        string file = Write("warning.xscn", "<Control Name=\"Root\">\n  <Gadget Name=\"G\" />\n</Control>");

        RunResult run = Run("check", file);

        string[] expected = [$"{file}(2,4): warning GX0107: {UnknownClass}", "1 file(s) checked, 0 error(s), 1 warning(s)"];
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void AFolderIsOrderedByRelativePathOnEveryOs()
    {
        string upper = Write("B.xscn", WarningScene);
        string lower = Write("a.xscn", WarningScene);
        string sub = Write("sub/z.xscn", WarningScene);
        string sub2 = Write("sub2/z.xscn", WarningScene);

        RunResult run = Run("check", folder);

        string[] expected =
        [
            $"{upper}(2,4): warning GX0107: {UnknownClass}",
            $"{lower}(2,4): warning GX0107: {UnknownClass}",
            $"{sub}(2,4): warning GX0107: {UnknownClass}",
            $"{sub2}(2,4): warning GX0107: {UnknownClass}",
            "4 file(s) checked, 0 error(s), 4 warning(s)",
        ];
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
    }

    [Fact]
    public void AMissingPathExitsTwo()
    {
        string missing = Path.Combine(folder, "missing.xscn");
        string[] expected = [$"{missing}: no such file or folder."];

        RunResult run = Run("check", missing);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AFileThatIsNotASceneOrResourceIsAUsageError()
    {
        string file = Write("notes.txt", "hello");
        string[] expected = [$"{file}: only .xscn and .xres files can be checked."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AMissingMetadataFileExitsTwo()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");
        string metadata = Path.Combine(folder, "missing.json");
        string[] expected = [$"{metadata}: no such file or folder."];

        RunResult run = Run("check", file, "--metadata", metadata);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AFolderThatCannotBeEnumeratedExitsTwoWithoutAStackTrace()
    {
        Write("a.xscn", "<Control Name=\"Root\" />");
        string[] expected = [$"{folder}: cannot be read."];

        RunResult run = RunWith(_ => throw new IOException("boom"), "check", folder);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
        Assert.DoesNotContain("Unhandled exception", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderEnumerationFailureAfterSomeFilesIsAccessDenied()
    {
        string[] expected = [$"{folder}: access denied."];

        RunResult run = RunWith(OnePathThenDenied, "check", folder);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void ASubfolderThatCannotBeEnumeratedNamesThatFolder()
    {
        Write("sub/a.xscn", "<Control Name=\"Root\" />");
        string[] expected = [$"{Path.Combine(folder, "sub")}: access denied."];

        RunResult run = RunWith(
            path =>
                path.EndsWith("sub", StringComparison.OrdinalIgnoreCase) ? throw new UnauthorizedAccessException() : Directory.EnumerateFiles(path),
            "check",
            folder
        );

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void TheLastDitchGuardPrintsOneLine()
    {
        string[] expected = ["godot-xaml: boom"];

        RunResult run = RunWith(_ => throw new InvalidOperationException("boom"), "check", folder);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AnEmptyOrBlankMetadataPathIsAUsageError()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");
        string[] expected = ["--metadata needs a file path."];

        RunResult empty = Run("check", file, "--metadata", "");
        RunResult blank = Run("check", file, "--metadata", "   ");

        Assert.Equal(2, empty.ExitCode);
        Assert.Equal("", empty.Output);
        Assert.Equal(expected, empty.ErrorLines);
        Assert.Equal(2, blank.ExitCode);
        Assert.Equal("", blank.Output);
        Assert.Equal(expected, blank.ErrorLines);
    }

    [Fact]
    public void ALatin1FileIsNotUtf8()
    {
        string file = WriteBytes("latin1.xscn", [.. Encoding.ASCII.GetBytes("<Control Name=\"Caf"), 0xE9, .. Encoding.ASCII.GetBytes("\" />")]);
        string[] expected = [$"{file}: not UTF-8 text."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AUtf16FileWithItsBomIsNotUtf8()
    {
        string file = WriteBytes("utf16.xscn", [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("<Control Name=\"Root\" />")]);
        string[] expected = [$"{file}: not UTF-8 text."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AUtf16FileWithoutItsBomIsNotUtf8()
    {
        string file = WriteBytes("utf16-no-bom.xscn", Encoding.Unicode.GetBytes("<Control Name=\"Root\" />"));
        string[] expected = [$"{file}: not UTF-8 text."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AFileWithANulByteIsNotUtf8()
    {
        string file = WriteBytes("nul.xscn", [.. Encoding.UTF8.GetBytes("<Control Name=\"Root\" />"), 0x00]);
        string[] expected = [$"{file}: not UTF-8 text."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AUtf16FileWithItsLittleEndianBomIsNotUtf8()
    {
        string file = WriteBytes("utf16-bom.xscn", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<Control Name=\"Root\" />")]);
        string[] expected = [$"{file}: not UTF-8 text."];

        RunResult run = Run("check", file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AUtf8BomDoesNotShiftColumns()
    {
        string file = WriteBytes(
            "bom.xscn",
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<Control Name=\"Root\">\n  <Label />\n</Control>")]
        );

        RunResult run = Run("check", file);

        string[] expected = [$"{file}(2,4): error GX0104: <Label> has no Name; every node needs one.", "1 file(s) checked, 1 error(s), 0 warning(s)"];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
    }

    [Fact]
    public void AFileThatCannotBeReadStopsTheRunBeforeAnyReport()
    {
        Write("first.xscn", "<Control Name=\"Root\">\n  <Label />\n</Control>");
        string locked = Write("second.xscn", "<Control Name=\"Root\" />");
        string[] expected = [$"{locked}: cannot be read."];

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            RunResult run = Run("check", folder);

            Assert.Equal(2, run.ExitCode);
            Assert.Equal("", run.Output);
            Assert.Equal(expected, run.ErrorLines);
        }
    }

    [Fact]
    public void AnUnreadableFileExitsTwo()
    {
        string file = Write("locked.xscn", "<Control Name=\"Root\" />");
        string[] expected = [$"{file}: cannot be read."];

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            RunResult run = Run("check", file);

            Assert.Equal(2, run.ExitCode);
            Assert.Equal("", run.Output);
            Assert.Equal(expected, run.ErrorLines);
        }
    }

    [Fact]
    public void ADuplicatePathIsCheckedOnce()
    {
        Write("a.xscn", "<Control Name=\"Root\" />");
        Write("b.xscn", "<Control Name=\"Root\" />");
        string sub = Write("sub/c.xscn", "<Control Name=\"Root\" />");

        RunResult run = Run("check", folder, sub);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["3 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
    }

    [Fact]
    public void AnUnknownOptionIsAParseError()
    {
        string[] expected = ["Unrecognized command or argument '--bogus'."];

        RunResult run = Run("check", "--bogus", "x");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void ALeadingDashPathIsAParseError()
    {
        string[] expected = ["Unrecognized command or argument '-name.xscn'."];

        RunResult run = Run("check", "-name.xscn");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void ADashPathBeforeADoubleDashIsStillAParseError()
    {
        Write("ok.xscn", "<Control Name=\"Root\" />");
        string[] expected = ["Unrecognized command or argument '-x.xscn'."];

        RunResult run = Run("check", "-x.xscn", "--", "ok.xscn");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void ACheckWithOnlyADoubleDashNeedsAPath()
    {
        string[] expected = ["Required argument missing for command: 'check'."];

        RunResult run = Run("check", "--");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AFolderChecksOnlyScenesAndResources()
    {
        Write("a.xscn", "<Control Name=\"Root\" />");
        Write("notes.txt", "hello");

        RunResult run = Run("check", folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
    }

    [Fact]
    public void AnUpperCaseExtensionIsAccepted()
    {
        string file = Write("UPPER.XSCN", "<Control Name=\"Root\" />");

        RunResult run = Run("check", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
    }

    [Fact]
    public void AMissingPathIsReportedBeforeAMissingMetadata()
    {
        string missing = Path.Combine(folder, "missing.xscn");
        string metadata = Path.Combine(folder, "missing.json");
        string[] expected = [$"{missing}: no such file or folder."];

        RunResult run = Run("check", missing, "--metadata", metadata);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void AMalformedMetadataFileExitsTwo()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");
        string metadata = Write("bad.json", "not json");

        RunResult run = Run("check", file, "--metadata", metadata);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Contains(metadata, Assert.Single(run.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderAsMetadataSaysItIsAFolder()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");
        string[] expected = [$"{folder}: is a folder, not a metadata file."];

        foreach (string command in new[] { "check", "format", "convert" })
        {
            RunResult run = Run(command, file, "--metadata", folder);

            Assert.Equal(expected, run.ErrorLines);
            Assert.Equal(2, run.ExitCode);
            Assert.Equal("", run.Output);
        }
    }

    [Fact]
    public void ReadingAFolderAsAFileIsAccessDenied()
    {
        using var error = new StringWriter();
        string[] expected = [$"{folder}: access denied."];

        string? text = SourceText.Read(folder, error);

        Assert.Null(text);
        Assert.Equal(expected, error.ToString().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void AGivenMetadataIsUsed()
    {
        string file = Write("clean.xscn", "<Control Name=\"Root\" />");
        string metadata = Write("small.json", "{\"version\": \"4.7\", \"godot\": \"4.7\", \"globalEnums\": {}, \"classes\": {}}");

        RunResult run = Run("check", file, "--metadata", metadata);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        Assert.Contains("warning GX0107", run.Lines[0], StringComparison.Ordinal);
        Assert.Equal("1 file(s) checked, 0 error(s), 1 warning(s)", run.Lines[1]);
    }

    private const string WarningScene = "<Control Name=\"Root\">\n  <Gadget Name=\"G\" />\n</Control>";

    private static IEnumerable<string> OnePathThenDenied(string folder)
    {
        yield return Path.Combine(folder, "a.xscn");
        throw new UnauthorizedAccessException();
    }

    private string Write(string relativePath, string text) => WriteBytes(relativePath, Encoding.UTF8.GetBytes(text));

    private string WriteBytes(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static RunResult Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliProgram.Run(args, output, error);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }

    private static RunResult RunWith(Func<string, IEnumerable<string>> enumerate, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliProgram.Run(args, output, error, new CliSeams(enumerate, Environment.CurrentDirectory, MetadataVersions.Bundled));
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record RunResult(int ExitCode, string Output, string Error)
    {
        public string[] Lines => Split(Output);

        public string[] ErrorLines => Split(Error);

        private static string[] Split(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
