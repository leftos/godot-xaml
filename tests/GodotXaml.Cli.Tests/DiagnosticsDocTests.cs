using System.Reflection;
using System.Text.RegularExpressions;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;

namespace GodotXaml.Cli.Tests;

public sealed class DiagnosticsDocTests
{
    [Theory]
    [InlineData("### GX0104", "GX0104")]
    [InlineData("## GXC0001 Unrepresentable attribute", "GXC0001")]
    [InlineData("#### GXP0008", "GXP0008")]
    public void AHeadingNamingACodeIsRead(string heading, string code) => Assert.Equal([code], DiagnosticsDoc.DocumentedCodes(heading));

    [Theory]
    [InlineData("GX0104")]
    [InlineData("##### GX0104")]
    public void ALineThatIsNotACodeHeadingIsNotRead(string line) => Assert.Empty(DiagnosticsDoc.DocumentedCodes(line));

    [Fact]
    public void EveryDefinedCodeIsDocumented()
    {
        const string Reference = "docs/format/diagnostics.md";
        string full = RepoRoot.PathOf(Reference.Split('/'));
        Assert.True(File.Exists(full), $"{Reference} does not exist");

        HashSet<string> defined = DefinedCodes();
        IReadOnlySet<string> documented = DiagnosticsDoc.DocumentedCodes(File.ReadAllText(full));

        string[] missing = [.. defined.Except(documented).Order(StringComparer.Ordinal)];
        string[] unknown = [.. documented.Except(defined).Order(StringComparer.Ordinal)];
        Assert.True(
            missing.Length == 0 && unknown.Length == 0,
            $"{Reference} is missing {string.Join(", ", missing)}; it names codes no table defines: {string.Join(", ", unknown)}."
        );
    }

    private static HashSet<string> DefinedCodes() =>
        [.. CodesOf(typeof(XscnCodes)), .. CodesOf(typeof(ConversionCodes)), .. CodesOf(typeof(CliCodes))];

    private static IEnumerable<string> CodesOf(Type table) =>
        table
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);
}

/// <summary>Reads the diagnostic codes out of the diagnostics reference's headings.</summary>
internal static partial class DiagnosticsDoc
{
    /// <summary>The codes the headings of a diagnostics reference name.</summary>
    /// <param name="markdown">The reference's Markdown text.</param>
    /// <returns>The distinct codes, in no particular order.</returns>
    public static IReadOnlySet<string> DocumentedCodes(string markdown) =>
        markdown
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Select(line => Heading().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"^#{2,4} (GX[A-Z]?\d{4})\b")]
    private static partial Regex Heading();
}
