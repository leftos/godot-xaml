using System.Text;

namespace GodotXaml.Cli.Tests;

/// <summary>The tests that need the process's current directory, so the change is never seen by another test.</summary>
[Collection("working directory")]
public sealed class WorkingDirectoryTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "godot-xaml-cli-working-directory", Guid.NewGuid().ToString("N"));
    private readonly string previous = Directory.GetCurrentDirectory();

    /// <summary>Creates the test's own folder and makes it the current directory.</summary>
    public WorkingDirectoryTests()
    {
        Directory.CreateDirectory(folder);
        Directory.SetCurrentDirectory(folder);
    }

    /// <summary>Restores the current directory and removes the test's folder.</summary>
    public void Dispose()
    {
        Directory.SetCurrentDirectory(previous);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADotSlashPathReachesALeadingDashFile()
    {
        Write("-name.xscn", "<Control Name=\"Root\" />");

        RunResult run = Run("check", "./-name.xscn");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
    }

    [Fact]
    public void CheckDoesNotExpandAResponseFile()
    {
        Write("@resp.xscn", "<Control Name=\"Root\" />");

        RunResult run = Run("check", "@resp.xscn");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void CheckDoesNotReadAResponseFileItsNameStartsAt()
    {
        Write("@resp.xscn", "<Control Name=\"Root\" />");
        Write("resp.xscn", "other.xscn");
        Write("other.xscn", "<Control Name=\"Root\">\n  <Gadget Name=\"G\" />\n</Control>");

        RunResult run = Run("check", "@resp.xscn");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void CheckAcceptsADashPathAfterADoubleDash()
    {
        Write("-x.xscn", "<Control Name=\"Root\" />");

        RunResult run = Run("check", "--", "-x.xscn");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["1 file(s) checked, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
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

    private sealed record RunResult(int ExitCode, string Output, string Error)
    {
        public string[] Lines => Split(Output);

        public string[] ErrorLines => Split(Error);

        private static string[] Split(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>The collection the current-directory tests share: no other test collection runs while one of them does.</summary>
[CollectionDefinition("working directory", DisableParallelization = true)]
public sealed class WorkingDirectoryCollectionDefinition { }
